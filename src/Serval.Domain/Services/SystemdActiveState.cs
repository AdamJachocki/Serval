namespace Serval.Domain.Services;

public enum SystemdActiveStateKind
{
    Unknown,
    Active,
    Reloading,
    Inactive,
    Failed,
    Activating,
    Deactivating,
    Maintenance,
    Refreshing,
}

/// <summary>
/// Represents a systemd unit active state while retaining the value reported by systemd.
/// </summary>
public sealed record SystemdActiveState
{
    public SystemdActiveState(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value;
        Kind = value switch
        {
            "active" => SystemdActiveStateKind.Active,
            "reloading" => SystemdActiveStateKind.Reloading,
            "inactive" => SystemdActiveStateKind.Inactive,
            "failed" => SystemdActiveStateKind.Failed,
            "activating" => SystemdActiveStateKind.Activating,
            "deactivating" => SystemdActiveStateKind.Deactivating,
            "maintenance" => SystemdActiveStateKind.Maintenance,
            "refreshing" => SystemdActiveStateKind.Refreshing,
            _ => SystemdActiveStateKind.Unknown,
        };
    }

    public string Value { get; }

    public SystemdActiveStateKind Kind { get; }

    public override string ToString() => Value;
}
