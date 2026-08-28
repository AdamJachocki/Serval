using Serval.Domain.Services;

namespace Serval.Application.Services;

/// <summary>
/// Represents the result of looking up a system service for inspection.
/// </summary>
public abstract record ServiceInspectionResult
{
    private ServiceInspectionResult()
    {
    }

    /// <summary>
    /// Represents a successful service lookup.
    /// </summary>
    public sealed record Found : ServiceInspectionResult
    {
        public Found(SystemService service)
        {
            ArgumentNullException.ThrowIfNull(service);

            Service = service;
        }

        public SystemService Service { get; }
    }

    /// <summary>
    /// Represents a lookup for a service that does not exist.
    /// </summary>
    public sealed record NotFound : ServiceInspectionResult
    {
        public NotFound(SystemServiceId serviceId)
        {
            ArgumentNullException.ThrowIfNull(serviceId);

            ServiceId = serviceId;
        }

        public SystemServiceId ServiceId { get; }
    }
}
