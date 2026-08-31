using Serval.Domain.Services;

namespace Serval.Application.Services;

/// <summary>
/// Provides read-only access to the inventory of system-level services.
/// </summary>
public interface ISystemServiceInventory
{
    /// <summary>
    /// Lists the services available for inspection.
    /// </summary>
    Task<IReadOnlyList<SystemService>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inspects the service identified by <paramref name="serviceId" />.
    /// </summary>
    Task<ServiceInspectionResult> InspectAsync(
        SystemServiceId serviceId,
        CancellationToken cancellationToken);
}
