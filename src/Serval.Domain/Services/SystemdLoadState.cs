namespace Serval.Domain.Services;

public enum SystemdLoadStateKind
{
    Unknown,
    Loaded,
    Error,
    NotFound,
    BadSetting,
    Masked,
    Stub,
    Merged,
}

/// <summary>
/// Represents a systemd unit load state while retaining the value reported by systemd.
/// </summary>
public sealed record SystemdLoadState
{
    public SystemdLoadState(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value;
        Kind = value switch
        {
            "loaded" => SystemdLoadStateKind.Loaded,
            "error" => SystemdLoadStateKind.Error,
            "not-found" => SystemdLoadStateKind.NotFound,
            "bad-setting" => SystemdLoadStateKind.BadSetting,
            "masked" => SystemdLoadStateKind.Masked,
            "stub" => SystemdLoadStateKind.Stub,
            "merged" => SystemdLoadStateKind.Merged,
            _ => SystemdLoadStateKind.Unknown,
        };
    }

    public string Value { get; }

    public SystemdLoadStateKind Kind { get; }

    public override string ToString() => Value;
}
