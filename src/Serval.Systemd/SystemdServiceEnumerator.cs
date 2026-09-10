using Serval.Domain.Services;
using Serval.Systemd.DBus;
using static Serval.Systemd.SystemdServiceIdentity;

namespace Serval.Systemd;

// Internal discovery only. Agent must authorize canonical Id and all Names before exposure.
internal sealed class SystemdServiceEnumerator
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private readonly Func<CancellationToken, Task<ISystemdDbusTransport>> _connect;
    private readonly TimeProvider _timeProvider;

    internal SystemdServiceEnumerator()
        : this(async token => await SystemdDbusTransport.ConnectAsync(Deadline, token)
            .ConfigureAwait(false), TimeProvider.System)
    {
    }

    internal SystemdServiceEnumerator(
        Func<CancellationToken, Task<ISystemdDbusTransport>> connect, TimeProvider timeProvider)
    {
        _connect = connect;
        _timeProvider = timeProvider;
    }

    internal async Task<ServiceEnumerationSnapshot> EnumerateAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Deadline, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            await using var transport = await _connect(linked.Token).ConfigureAwait(false);
            var snapshot = await new Enumeration(transport, linked.Token).RunAsync().ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.Timeout);
        }
    }

    private sealed class Enumeration(ISystemdDbusTransport transport, CancellationToken token)
    {
        private readonly Dictionary<string, EnumeratedSystemService> _services = new(StringComparer.Ordinal);
        private readonly HashSet<string> _knownNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _retriedNames = new(StringComparer.Ordinal);
        private readonly Dictionary<SystemdUnitReference, SystemdServiceIdentity> _resolved = [];
        private readonly Dictionary<SystemServiceId, SystemServiceId> _canonicalByName = [];
        private readonly SortedSet<string> _templates = new(StringComparer.Ordinal);

        internal async Task<ServiceEnumerationSnapshot> RunAsync()
        {
            SystemdCompatibility.ValidateVersion(await transport.GetManagerVersionAsync(token).ConfigureAwait(false));
            var files = await transport.ListUnitFilesAsync(token).ConfigureAwait(false);
            var installed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                // Linux separators, including when tests execute on Windows; no filesystem access.
                var name = file.Path[(file.Path.LastIndexOf('/') + 1)..];
                if (!name.EndsWith(".service", StringComparison.Ordinal))
                {
                    continue;
                }

                ValidateName(name);
                if (IsTemplate(name))
                {
                    _templates.Add(name);
                }
                else
                {
                    installed.Add(name);
                }
            }

            var loaded = await transport.ListServiceUnitsAsync(token).ConfigureAwait(false);
            await ResolveAllAsync(loaded, retry: true).ConfigureAwait(false);
            var missing = installed.Where(name => !_knownNames.Contains(name) && !_retriedNames.Contains(name)).ToArray();
            if (missing.Length > 0)
            {
                var entries = await LookupAsync(missing).ConfigureAwait(false);
                await ResolveAllAsync(entries, retry: true).ConfigureAwait(false);
                foreach (var name in missing.Where(name => !_knownNames.Contains(name)))
                {
                    await RetryAsync(name).ConfigureAwait(false);
                }
            }

            token.ThrowIfCancellationRequested();
            return new ServiceEnumerationSnapshot(
                Array.AsReadOnly(_services.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value).ToArray()),
                Array.AsReadOnly(_templates.Select(name => new SystemServiceId(name)).ToArray()));
        }

        private async Task ResolveAllAsync(IReadOnlyList<SystemdListedUnit> entries, bool retry)
        {
            foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                ValidateName(entry.Name);
                if (entry.FollowedUnit.Length > 0)
                {
                    ValidateName(entry.FollowedUnit);
                }

                if (IsTemplate(entry.Name))
                {
                    _templates.Add(entry.Name);
                    continue;
                }

                if (_resolved.TryGetValue(entry.Unit, out var resolved))
                {
                    resolved.RequireName(entry.Name);
                    continue;
                }

                SystemdUnitProperties? properties = null;
                try
                {
                    properties = await transport.ReadUnitPropertiesAsync(entry.Unit, token)
                        .ConfigureAwait(false);
                }
                catch (SystemdDbusException exception) when (IsAbsent(exception))
                {
                    // Only disappearance is retryable, within the original deadline.
                }

                if (properties is null || properties.LoadState == "not-found")
                {
                    if (retry)
                    {
                        await RetryAsync(entry.Name).ConfigureAwait(false);
                    }

                    continue;
                }

                var identity = new SystemdServiceIdentity(properties);
                identity.RequireName(entry.Name);
                Add(properties, identity);
                _resolved.Add(entry.Unit, identity);
            }
        }

        private async Task RetryAsync(string name)
        {
            if (!_retriedNames.Add(name))
            {
                return;
            }

            var entries = await LookupAsync([name]).ConfigureAwait(false);
            await ResolveAllAsync(entries, retry: false).ConfigureAwait(false);
        }

        private async Task<IReadOnlyList<SystemdListedUnit>> LookupAsync(string[] names)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await transport.ListUnitsByNamesAsync(
                    names.Select(name => new SystemServiceId(name)).ToArray(), token).ConfigureAwait(false);
            }
            catch (SystemdDbusException exception) when (
                exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
                exception.RemoteErrorName == "org.freedesktop.systemd1.NoSuchUnit")
            {
                return [];
            }
        }

        private void Add(SystemdUnitProperties properties, SystemdServiceIdentity identity)
        {
            var id = identity.Id;
            var names = identity.Names.ToArray();
            foreach (var name in names)
            {
                // Each name has one canonical owner; conflicts and cycles fail the snapshot.
                if (_canonicalByName.TryGetValue(name, out var owner) && owner != id)
                {
                    throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
                }

                _canonicalByName[name] = id;
            }

            try
            {
                var service = new SystemService(id, properties.Description,
                    new SystemdLoadState(properties.LoadState),
                    new SystemdActiveState(properties.ActiveState), new SystemdSubState(properties.SubState));
                if (_services.TryGetValue(id.Value, out var previous))
                {
                    names = names.Concat(previous.Names).Distinct().ToArray();
                    service = previous.Service;
                }

                _services[id.Value] = new EnumeratedSystemService(service,
                    Array.AsReadOnly(names.OrderBy(name => name.Value, StringComparer.Ordinal).ToArray()));
                _knownNames.UnionWith(names.Select(name => name.Value));
            }
            catch (ArgumentException)
            {
                throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
            }
        }
    }

    private static bool IsAbsent(SystemdDbusException exception) =>
        exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
        exception.RemoteErrorName is "org.freedesktop.systemd1.NoSuchUnit" or
            "org.freedesktop.DBus.Error.UnknownObject";
}
