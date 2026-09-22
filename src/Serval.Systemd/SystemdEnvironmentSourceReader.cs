using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using static Serval.Systemd.SystemdServiceIdentity;

namespace Serval.Systemd;

// Internal acquisition only. Agent authorization and application composition are intentionally absent.
internal sealed class SystemdEnvironmentSourceReader
{
    private readonly Func<CancellationToken, Task<ISystemdDbusTransport>> _connect;
    private readonly ISystemdSourceFileAccess _files;
    private readonly TimeProvider _timeProvider;

    internal SystemdEnvironmentSourceReader()
        : this(
            async token => await SystemdDbusTransport.ConnectAsync(
                EnvironmentReadLimits.OperationDeadline,
                token).ConfigureAwait(false),
            new LinuxSystemdSourceFileAccess(),
            TimeProvider.System)
    {
    }

    internal SystemdEnvironmentSourceReader(
        Func<CancellationToken, Task<ISystemdDbusTransport>> connect,
        ISystemdSourceFileAccess files,
        TimeProvider timeProvider)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal async Task<SystemdEnvironmentSourceReadResult> ReadAsync(
        SystemServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        _ = new SystemServiceId(serviceId.Value);
        if (IsTemplate(serviceId.Value))
            throw new ArgumentException("Source reading requires a concrete service identifier.", nameof(serviceId));
        cancellationToken.ThrowIfCancellationRequested();
        if (BuiltInProtectedServices.IsProtected(serviceId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(EnvironmentReadFailureCode.ProtectedTarget);
        }

        using var deadline = new CancellationTokenSource(EnvironmentReadLimits.OperationDeadline, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        SystemdEnvironmentSourceReadResult.Failure PreferCancellation(
            SystemdEnvironmentSourceReadResult.Failure failure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return deadline.IsCancellationRequested
                ? Failure(EnvironmentReadFailureCode.Timeout)
                : failure;
        }

        try
        {
            linked.Token.ThrowIfCancellationRequested();
            await using var transport = await _connect(linked.Token).ConfigureAwait(false);
            SystemdCompatibility.ValidateVersion(
                await transport.GetManagerVersionAsync(linked.Token).ConfigureAwait(false));
            var resolved = await SystemdServiceResolver.ResolveOnceAsync(transport, serviceId, linked.Token)
                .ConfigureAwait(false);
            if (resolved is null)
                return PreferCancellation(Failure(EnvironmentReadFailureCode.NotFound));
            if (BuiltInProtectedServices.IsProtected(resolved.Identity.Id, resolved.Identity.Names))
                return PreferCancellation(Failure(EnvironmentReadFailureCode.ProtectedTarget));

            SystemdEnvironmentProperties initial;
            try
            {
                initial = await transport.ReadEnvironmentPropertiesAsync(resolved.Unit, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (SystemdDbusException exception) when (IsDisappearance(exception))
            {
                return PreferCancellation(Failure(EnvironmentReadFailureCode.InconsistentSnapshot));
            }
            catch (SystemdDbusException exception) when (IsMissingRequiredProperty(exception))
            {
                return PreferCancellation(Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty));
            }
            var validationFailure = ValidateInitial(initial, resolved.Identity, serviceId);
            if (validationFailure is not null)
                return PreferCancellation(validationFailure);

            using var candidate = new SourceCandidate();
            foreach (var path in ConfigurationPaths(initial))
            {
                var observation = await _files.ObserveAsync(
                    path,
                    readContent: false,
                    allowMissing: false,
                    linked.Token).ConfigureAwait(false);
                candidate.Configuration.Add(observation);
            }

            var decoded = LoadedEnvironmentDecoder.Decode(initial.Environment.ToArray(), linked.Token);
            if (decoded is LoadedEnvironmentResult.Failure decodeFailure)
                return PreferCancellation(Failure(decodeFailure.Code, sourceId: 0));
            candidate.Manager = (LoadedEnvironmentResult.Success)decoded;
            candidate.TotalBytes = candidate.Manager.SourceBytes;

            for (var index = 0; index < initial.EnvironmentFiles.Count; index++)
            {
                linked.Token.ThrowIfCancellationRequested();
                var declaration = initial.EnvironmentFiles[index];
                var sourceId = index + 1;
                SystemdFileObservation observation;
                try
                {
                    var remainingBytes = EnvironmentReadLimits.MaxTotalSourceBytes - candidate.TotalBytes;
                    observation = await _files.ObserveAsync(
                        declaration.Path,
                        readContent: true,
                        allowMissing: declaration.IgnoreErrors,
                        linked.Token,
                        Math.Min(EnvironmentReadLimits.MaxSourceBytes, remainingBytes))
                        .ConfigureAwait(false);
                }
                catch (SystemdSourceFileException exception)
                {
                    return PreferCancellation(MapFileFailure(exception.Failure, sourceId));
                }

                candidate.Files.Add(observation);
                if (!observation.IsMissing)
                {
                    if (observation.ContentLength > EnvironmentReadLimits.MaxTotalSourceBytes - candidate.TotalBytes)
                        return PreferCancellation(Failure(EnvironmentReadFailureCode.LimitExceeded, sourceId: sourceId));
                    candidate.TotalBytes += observation.ContentLength;
                }
            }

            SystemdEnvironmentProperties final;
            try
            {
                final = await transport.ReadEnvironmentPropertiesAsync(resolved.Unit, linked.Token)
                    .ConfigureAwait(false);
            }
            catch (SystemdDbusException exception) when (
                IsDisappearance(exception) || IsMissingRequiredProperty(exception))
            {
                return PreferCancellation(Failure(EnvironmentReadFailureCode.InconsistentSnapshot));
            }
            catch (SystemdDbusException exception) when (
                exception.FailureKind is SystemdDbusFailureKind.MalformedReply or
                    SystemdDbusFailureKind.IncompatibleReply)
            {
                return PreferCancellation(Failure(EnvironmentReadFailureCode.InconsistentSnapshot));
            }

            if (!PropertiesEqual(initial, final))
                return PreferCancellation(Failure(EnvironmentReadFailureCode.InconsistentSnapshot));
            foreach (var observation in candidate.Configuration.Concat(candidate.Files))
                if (!await _files.IsStableAsync(observation, linked.Token).ConfigureAwait(false))
                    return PreferCancellation(Failure(EnvironmentReadFailureCode.InconsistentSnapshot));

            cancellationToken.ThrowIfCancellationRequested();
            linked.Token.ThrowIfCancellationRequested();
            return candidate.Publish(resolved.Identity.Id, initial.EnvironmentFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return PreferCancellation(Failure(EnvironmentReadFailureCode.Timeout));
        }
        catch (SystemdSourceFileException exception)
        {
            return PreferCancellation(MapFileFailure(exception.Failure, sourceId: null));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.Timeout && deadline.IsCancellationRequested)
        {
            return PreferCancellation(Failure(EnvironmentReadFailureCode.Timeout));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.LimitExceeded)
        {
            return PreferCancellation(Failure(EnvironmentReadFailureCode.LimitExceeded));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind is SystemdDbusFailureKind.MalformedReply or
                SystemdDbusFailureKind.IncompatibleReply or
                SystemdDbusFailureKind.UnsupportedVersion)
        {
            return PreferCancellation(Failure(
                EnvironmentReadFailureCode.UnsupportedConfiguration,
                EnvironmentUnsupportedReason.UnsupportedProperty));
        }
        catch (SystemdDbusException)
        {
            return PreferCancellation(Failure(EnvironmentReadFailureCode.TransportError));
        }
    }

    private static SystemdEnvironmentSourceReadResult.Failure? ValidateInitial(
        SystemdEnvironmentProperties properties,
        SystemdServiceIdentity resolved,
        SystemServiceId requested)
    {
        var identity = new SystemdServiceIdentity(new SystemdUnitProperties(
            properties.Id,
            properties.Names,
            string.Empty,
            properties.LoadState,
            "inactive",
            "dead"));
        if (!string.Equals(identity.Id.Value, resolved.Id.Value, StringComparison.Ordinal))
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        identity.RequireName(requested.Value);
        identity.RequireName(resolved.Id.Value);
        if (!identity.Names.SequenceEqual(resolved.Names))
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        if (properties.LoadState == "not-found")
            return Failure(EnvironmentReadFailureCode.NotFound);
        if (!string.Equals(properties.LoadState, "loaded", StringComparison.Ordinal))
            return Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty);
        if (properties.NeedDaemonReload)
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        if (properties.Transient)
            return Unsupported(EnvironmentUnsupportedReason.TransientUnit);
        if (string.Equals(properties.UnitFileState, "generated", StringComparison.Ordinal))
            return Unsupported(EnvironmentUnsupportedReason.GeneratedUnit);
        if (properties.UnsetEnvironment.Count != 0)
            return Unsupported(EnvironmentUnsupportedReason.UnsetEnvironment);
        if (properties.PassEnvironment.Count != 0)
            return Unsupported(EnvironmentUnsupportedReason.PassEnvironment);
        if (properties.EnvironmentFiles.Count + 1 > EnvironmentReadLimits.MaxSources ||
            properties.Environment.Count > EnvironmentReadLimits.MaxAssignments ||
            ConfigurationPaths(properties).Count > EnvironmentReadLimits.MaxConfigurationPaths)
            return Failure(EnvironmentReadFailureCode.LimitExceeded);
        if (properties.FragmentPath.Length == 0)
            return Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty);

        foreach (var path in ConfigurationPaths(properties))
        {
            if (SystemdSourcePath.ExceedsLimit(path))
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            var pathFailure = SystemdSourcePath.Validate(path);
            if (pathFailure is { } reason)
                return Unsupported(reason);
            if (SystemdSourcePath.IsGenerated(path))
                return Unsupported(EnvironmentUnsupportedReason.GeneratedUnit);
        }

        foreach (var file in properties.EnvironmentFiles)
        {
            if (SystemdSourcePath.ExceedsLimit(file.Path))
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            var pathFailure = SystemdSourcePath.Validate(file.Path);
            if (pathFailure is { } reason)
                return Unsupported(reason);
        }

        return null;
    }

    private static IReadOnlyList<string> ConfigurationPaths(SystemdEnvironmentProperties properties)
    {
        if (properties.FragmentPath.Length == 0)
            return properties.DropInPaths;
        var paths = new string[properties.DropInPaths.Count + 1];
        paths[0] = properties.FragmentPath;
        for (var index = 0; index < properties.DropInPaths.Count; index++)
            paths[index + 1] = properties.DropInPaths[index];
        return paths;
    }

    private static bool PropertiesEqual(SystemdEnvironmentProperties left, SystemdEnvironmentProperties right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        left.Names.SequenceEqual(right.Names, StringComparer.Ordinal) &&
        string.Equals(left.LoadState, right.LoadState, StringComparison.Ordinal) &&
        string.Equals(left.FragmentPath, right.FragmentPath, StringComparison.Ordinal) &&
        left.DropInPaths.SequenceEqual(right.DropInPaths, StringComparer.Ordinal) &&
        left.NeedDaemonReload == right.NeedDaemonReload &&
        left.Transient == right.Transient &&
        string.Equals(left.UnitFileState, right.UnitFileState, StringComparison.Ordinal) &&
        left.Environment.SequenceEqual(right.Environment, StringComparer.Ordinal) &&
        left.EnvironmentFiles.SequenceEqual(right.EnvironmentFiles) &&
        left.UnsetEnvironment.SequenceEqual(right.UnsetEnvironment, StringComparer.Ordinal) &&
        left.PassEnvironment.SequenceEqual(right.PassEnvironment, StringComparer.Ordinal);

    private static bool IsDisappearance(SystemdDbusException exception) =>
        exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
        exception.RemoteErrorName is "org.freedesktop.systemd1.NoSuchUnit" or
            "org.freedesktop.DBus.Error.UnknownObject";

    private static bool IsMissingRequiredProperty(SystemdDbusException exception) =>
        exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
        exception.RemoteErrorName is "org.freedesktop.DBus.Error.UnknownProperty" or
            "org.freedesktop.DBus.Error.UnknownInterface";

    private static SystemdEnvironmentSourceReadResult.Failure MapFileFailure(
        SystemdSourceFileFailure failure,
        int? sourceId) => failure switch
        {
            SystemdSourceFileFailure.Missing or SystemdSourceFileFailure.Unavailable =>
                Failure(EnvironmentReadFailureCode.SourceUnavailable, sourceId: sourceId),
            SystemdSourceFileFailure.UnsafePath =>
                Unsupported(EnvironmentUnsupportedReason.UnsafePath, sourceId),
            SystemdSourceFileFailure.Inconsistent =>
                Failure(EnvironmentReadFailureCode.InconsistentSnapshot, sourceId: sourceId),
            SystemdSourceFileFailure.LimitExceeded =>
                Failure(EnvironmentReadFailureCode.LimitExceeded, sourceId: sourceId),
            _ => Failure(EnvironmentReadFailureCode.SourceUnavailable, sourceId: sourceId),
        };

    private static SystemdEnvironmentSourceReadResult.Failure Unsupported(
        EnvironmentUnsupportedReason reason,
        int? sourceId = null) =>
        Failure(EnvironmentReadFailureCode.UnsupportedConfiguration, reason, sourceId);

    private static SystemdEnvironmentSourceReadResult.Failure Failure(
        EnvironmentReadFailureCode code,
        EnvironmentUnsupportedReason? reason = null,
        int? sourceId = null) => new(code, reason, sourceId);

    private sealed class SourceCandidate : IDisposable
    {
        internal LoadedEnvironmentResult.Success? Manager { get; set; }
        internal List<SystemdFileObservation> Configuration { get; } = [];
        internal List<SystemdFileObservation> Files { get; } = [];
        internal int TotalBytes { get; set; }

        internal SystemdEnvironmentSourceReadResult.Success Publish(
            SystemServiceId canonicalId,
            IReadOnlyList<SystemdEnvironmentFile> declarations)
        {
            var manager = Manager ?? throw new InvalidOperationException("The candidate has no manager source.");
            var published = new List<SystemdEnvironmentFileSource>(Files.Count);
            try
            {
                for (var index = 0; index < Files.Count; index++)
                {
                    var observation = Files[index];
                    published.Add(new SystemdEnvironmentFileSource(
                        index + 1,
                        declarations[index].IgnoreErrors,
                        observation.IsMissing,
                        observation.IsMissing ? null : observation.TakeContent()));
                }

                var result = new SystemdEnvironmentSourceReadResult.Success(canonicalId, manager, published);
                Manager = null;
                published.Clear();
                return result;
            }
            finally
            {
                foreach (var source in published)
                    source.Dispose();
            }
        }

        public void Dispose()
        {
            for (var index = Files.Count - 1; index >= 0; index--)
                Files[index].Dispose();
            for (var index = Configuration.Count - 1; index >= 0; index--)
                Configuration[index].Dispose();
            Manager?.Dispose();
        }
    }
}
