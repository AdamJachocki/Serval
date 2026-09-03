namespace Serval.Domain.Services;

/// <summary>
/// Identifies a discovered service by its canonical systemd unit name.
/// </summary>
public sealed record SystemServiceId
{
    private const string ServiceSuffix = ".service";
    private const int MaximumUnitNameLength = 255;

    public SystemServiceId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IsValidServiceUnitName(value))
        {
            throw new ArgumentException("The value must be a valid systemd service unit name.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsValidServiceUnitName(string value)
    {
        if (value.Length is 0 or > MaximumUnitNameLength ||
            !value.EndsWith(ServiceSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var prefixLength = value.Length - ServiceSuffix.Length;
        if (prefixLength == 0)
        {
            return false;
        }

        if (value.IndexOf('@', 0, prefixLength) == 0)
        {
            return false;
        }

        for (var index = 0; index < prefixLength; index++)
        {
            var character = value[index];
            if (character == '@')
            {
                continue;
            }

            if (!IsValidUnitNameCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidUnitNameCharacter(char character) =>
        character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            ':' or '-' or '_' or '.' or '\\';
}
