using System.Text;
using Serval.Application.Services;

namespace Serval.Systemd;

internal static class SystemdSourcePath
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] GeneratorRoots =
    [
        "/run/systemd/generator",
        "/run/systemd/generator.early",
        "/run/systemd/generator.late",
    ];

    internal static EnvironmentUnsupportedReason? Validate(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            return EnvironmentUnsupportedReason.UnsafePath;
        if (path.IndexOfAny(['*', '?', '[', ']']) >= 0)
            return EnvironmentUnsupportedReason.PathPattern;
        if (path.Contains('%', StringComparison.Ordinal))
            return EnvironmentUnsupportedReason.UnresolvedSpecifier;
        if (path.Any(character => character == '\0' || char.IsControl(character)))
            return EnvironmentUnsupportedReason.UnsafePath;

        try
        {
            if (StrictUtf8.GetByteCount(path) > EnvironmentReadLimits.MaxPathBytes)
                return EnvironmentUnsupportedReason.UnsafePath;
        }
        catch (EncoderFallbackException)
        {
            return EnvironmentUnsupportedReason.UnsafePath;
        }

        var components = path.Split('/');
        if (components.Skip(1).Any(component => component.Length == 0 || component is "." or ".."))
            return EnvironmentUnsupportedReason.UnsafePath;
        return null;
    }

    internal static bool ExceedsLimit(string path)
    {
        try
        {
            return StrictUtf8.GetByteCount(path) > EnvironmentReadLimits.MaxPathBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool IsGenerated(string path) => GeneratorRoots.Any(root =>
        string.Equals(path, root, StringComparison.Ordinal) ||
        path.StartsWith(root + "/", StringComparison.Ordinal));
}
