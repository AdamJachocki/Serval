namespace Serval.Domain.Services;

/// <summary>
/// Represents the unit-type-specific sub-state reported by systemd.
/// </summary>
public sealed record SystemdSubState
{
    public SystemdSubState(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
