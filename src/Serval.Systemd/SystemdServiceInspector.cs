using Serval.Domain.Services;
using Serval.Systemd.DBus;
using static Serval.Systemd.SystemdServiceIdentity;

namespace Serval.Systemd;

// Not registered with Web or IPC; these results have not passed Agent policy checks.
internal sealed class SystemdServiceInspector
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private readonly Func<CancellationToken, Task<ISystemdDbusTransport>> _connect;
    private readonly TimeProvider _timeProvider;

    internal SystemdServiceInspector()
        : this(async token => await SystemdDbusTransport.ConnectAsync(Deadline, token)
            .ConfigureAwait(false), TimeProvider.System)
    {
    }

    internal SystemdServiceInspector(
        Func<CancellationToken, Task<ISystemdDbusTransport>> connect, TimeProvider timeProvider)
    {
        _connect = connect;
        _timeProvider = timeProvider;
    }

    internal async Task<SystemdServiceInspectionResult> InspectAsync(
        SystemServiceId serviceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        // Revalidate at the adapter boundary before connecting to the system bus.
        _ = new SystemServiceId(serviceId.Value);
        if (IsTemplate(serviceId.Value))
        {
            throw new ArgumentException("Inspection requires a concrete service identifier.", nameof(serviceId));
        }

        if (ServalPrivilegedUnits.IsExcluded(serviceId))
        {
            return new SystemdServiceInspectionResult.NotFound(serviceId);
        }

        using var deadline = new CancellationTokenSource(Deadline, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            await using var transport = await _connect(linked.Token).ConfigureAwait(false);
            SystemdCompatibility.ValidateVersion(
                await transport.GetManagerVersionAsync(linked.Token).ConfigureAwait(false));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                linked.Token.ThrowIfCancellationRequested();
                var result = await ResolveAsync(transport, serviceId, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (result is not null)
                {
                    return result;
                }
            }

            return new SystemdServiceInspectionResult.NotFound(serviceId);
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

    private static async Task<SystemdServiceInspectionResult?> ResolveAsync(
        ISystemdDbusTransport transport, SystemServiceId requested, CancellationToken token)
    {
        IReadOnlyList<SystemdListedUnit> units;
        try
        {
            units = await transport.ListUnitsByNamesAsync([requested], token).ConfigureAwait(false);
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
            exception.RemoteErrorName == "org.freedesktop.systemd1.NoSuchUnit")
        {
            return null;
        }

        if (units.Count == 0)
        {
            return null;
        }

        if (units.Count != 1 || IsTemplate(units[0].Name))
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }

        token.ThrowIfCancellationRequested();
        SystemdUnitProperties properties;
        try
        {
            properties = await transport.ReadUnitPropertiesAsync(units[0].Unit, token).ConfigureAwait(false);
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
            exception.RemoteErrorName is "org.freedesktop.systemd1.NoSuchUnit" or
                "org.freedesktop.DBus.Error.UnknownObject")
        {
            return null;
        }

        try
        {
            var identity = new SystemdServiceIdentity(properties);
            identity.RequireName(requested.Value);
            identity.RequireName(units[0].Name);

            if (ServalPrivilegedUnits.IsExcluded(identity.Id, identity.Names))
            {
                return new SystemdServiceInspectionResult.NotFound(requested);
            }

            var service = new SystemService(identity.Id, properties.Description,
                new SystemdLoadState(properties.LoadState), new SystemdActiveState(properties.ActiveState),
                new SystemdSubState(properties.SubState));
            return properties.LoadState == "not-found" ? null :
                new SystemdServiceInspectionResult.Found(service, identity.Names);
        }
        catch (ArgumentException)
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }
    }
}
