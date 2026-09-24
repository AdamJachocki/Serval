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
                var resolved = await SystemdServiceResolver.ResolveOnceAsync(transport, serviceId, linked.Token)
                    .ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (resolved is not null)
                {
                    if (ServalPrivilegedUnits.IsExcluded(resolved.Identity.Id, resolved.Identity.Names))
                        return new SystemdServiceInspectionResult.NotFound(serviceId);
                    var properties = resolved.Properties;
                    try
                    {
                        var service = new SystemService(resolved.Identity.Id, properties.Description,
                            new SystemdLoadState(properties.LoadState), new SystemdActiveState(properties.ActiveState),
                            new SystemdSubState(properties.SubState));
                        return new SystemdServiceInspectionResult.Found(service, resolved.Identity.Names);
                    }
                    catch (ArgumentException)
                    {
                        throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
                    }
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

}
