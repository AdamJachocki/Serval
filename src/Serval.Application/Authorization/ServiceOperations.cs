namespace Serval.Application.Authorization;

public static class ServiceOperations
{
    public const string View = "Service.View";
    public const string EnvironmentView = "Environment.View";
    public const string EnvironmentReveal = "Environment.Reveal";

    public static bool IsSupported(string operation) =>
        operation is View or EnvironmentView or EnvironmentReveal;

    public static bool IsValidVariableName(string? name) =>
        name is { Length: > 0 and <= 4096 } &&
        (char.IsAsciiLetter(name[0]) || name[0] == '_') &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
