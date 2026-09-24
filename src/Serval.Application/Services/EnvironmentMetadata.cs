namespace Serval.Application.Services;

public enum EnvironmentSourceKind { ManagerEnvironment, EnvironmentFile }

/// <summary>Request-local source identity; deliberately contains no path or source text.</summary>
public sealed class EnvironmentSourceMetadata
{
    public EnvironmentSourceMetadata(int id, EnvironmentSourceKind kind, bool isOptional = false, bool isMissing = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if ((isOptional || isMissing) && kind != EnvironmentSourceKind.EnvironmentFile || isMissing && !isOptional)
            throw new ArgumentException("Invalid source state.");
        Id = id;
        Kind = kind;
        IsOptional = isOptional;
        IsMissing = isMissing;
    }

    public int Id { get; }
    public EnvironmentSourceKind Kind { get; }
    public bool IsOptional { get; }
    public bool IsMissing { get; }
}

/// <summary>Provenance of the winning assignment, without its value.</summary>
public sealed class EnvironmentVariableMetadata
{
    public EnvironmentVariableMetadata(string name, int winningSourceId)
    {
        if (string.IsNullOrEmpty(name) ||
            !(char.IsAsciiLetter(name[0]) || name[0] == '_') ||
            name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new ArgumentException("Invalid environment variable name.", nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(winningSourceId);
        Name = name;
        WinningSourceId = winningSourceId;
    }

    public string Name { get; }
    public int WinningSourceId { get; }
}
