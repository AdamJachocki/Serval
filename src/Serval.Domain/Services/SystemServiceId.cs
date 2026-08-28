namespace Serval.Domain.Services;

/// <summary>
/// Identifies a discovered service by its canonical systemd unit name.
/// </summary>
public sealed record SystemServiceId
{
    public SystemServiceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
