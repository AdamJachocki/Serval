using Serval.Application.Services;
using Serval.Domain.Services;

namespace Serval.Systemd;

/// <summary>
/// Provides the application-facing, read-only system service inventory.
/// </summary>
public sealed class SystemdServiceInventory : ISystemServiceInventory
{
    private readonly Func<CancellationToken, Task<ServiceEnumerationSnapshot>> _enumerate;
    private readonly Func<SystemServiceId, CancellationToken, Task<SystemdServiceInspectionResult>> _inspect;

    public SystemdServiceInventory()
        : this(
            static token => new SystemdServiceEnumerator().EnumerateAsync(token),
            static (serviceId, token) => new SystemdServiceInspector().InspectAsync(serviceId, token))
    {
    }

    internal SystemdServiceInventory(
        Func<CancellationToken, Task<ServiceEnumerationSnapshot>> enumerate,
        Func<SystemServiceId, CancellationToken, Task<SystemdServiceInspectionResult>> inspect)
    {
        ArgumentNullException.ThrowIfNull(enumerate);
        ArgumentNullException.ThrowIfNull(inspect);

        _enumerate = enumerate;
        _inspect = inspect;
    }

    public async Task<IReadOnlyList<SystemService>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await _enumerate(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var services = snapshot.Services
            .OrderBy(item => item.Service.Id.Value, StringComparer.Ordinal)
            .Select(item => ToApplicationService(item.Service, item.IsProtected))
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(services);
    }

    public async Task<ServiceInspectionResult> InspectAsync(
        SystemServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _inspect(serviceId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return result switch
        {
            SystemdServiceInspectionResult.Found found =>
                new ServiceInspectionResult.Found(
                    ToApplicationService(found.Service, found.IsProtected)),
            SystemdServiceInspectionResult.NotFound notFound =>
                new ServiceInspectionResult.NotFound(notFound.ServiceId),
            _ => throw new InvalidOperationException("Unsupported system service inspection result."),
        };
    }

    private static SystemService ToApplicationService(SystemService service, bool isProtected) =>
        new(
            service.Id,
            service.Description,
            service.LoadState,
            service.ActiveState,
            service.SubState,
            isProtected);
}
