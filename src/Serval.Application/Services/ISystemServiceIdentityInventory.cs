using Serval.Domain.Services;

namespace Serval.Application.Services;

/// <summary>Validated identity metadata for Agent policy checks; not an authorized Web result.</summary>
public interface ISystemServiceIdentityInventory
{
    Task<IReadOnlyList<SystemServiceIdentity>> ListIdentitiesAsync(CancellationToken cancellationToken);

    Task<ServiceIdentityInspectionResult> InspectIdentityAsync(
        SystemServiceId serviceId, CancellationToken cancellationToken);
}

public sealed record SystemServiceIdentity
{
    public SystemServiceIdentity(SystemService service, IReadOnlyList<SystemServiceId> names)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(names);

        var copy = names.ToArray();
        if (copy.Length == 0 || copy.Any(name => name is null) ||
            !copy.Contains(service.Id) ||
            !copy.Select(name => name.Value).SequenceEqual(
                copy.Select(name => name.Value).Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new ArgumentException("Service identity names are invalid.", nameof(names));
        }

        Service = service;
        Names = Array.AsReadOnly(copy);
    }

    public SystemService Service { get; }

    public IReadOnlyList<SystemServiceId> Names { get; }
}

public abstract record ServiceIdentityInspectionResult
{
    private ServiceIdentityInspectionResult() { }

    public sealed record Found(SystemServiceIdentity Identity) : ServiceIdentityInspectionResult;

    public sealed record NotFound(SystemServiceId ServiceId) : ServiceIdentityInspectionResult;
}
