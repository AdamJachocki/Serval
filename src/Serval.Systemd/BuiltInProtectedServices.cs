using System.Collections.Frozen;
using Serval.Domain.Services;

namespace Serval.Systemd;

// Classification only; not an authorization decision or a configurable policy.
internal static class BuiltInProtectedServices
{
    // Every exact name and template family is explained in docs/protected-services.md.
    private static readonly FrozenSet<string> ExactNames = new[]
    {
        "serval-agent.service",
        "dbus.service",
        "dbus-broker.service",
        "systemd-journald.service",
        "systemd-logind.service",
        "systemd-udevd.service",
        "systemd-networkd.service",
        "systemd-resolved.service",
        "NetworkManager.service",
        "networking.service",
        "ssh.service",
        "sshd.service",
        "polkit.service",
        "systemd-user-sessions.service",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TemplateFamilies = new[]
    {
        "serval-agent",
        "systemd-journald",
        "ssh",
        "sshd",
        "user",
    }.ToFrozenSet(StringComparer.Ordinal);

    internal static bool IsProtected(SystemServiceId id) =>
        ExactNames.Contains(id.Value) || IsProtectedTemplateFamily(id.Value);

    internal static bool IsProtected(SystemServiceId canonicalId, IReadOnlyList<SystemServiceId> names) =>
        IsProtected(canonicalId) || names.Any(IsProtected);

    private static bool IsProtectedTemplateFamily(string name)
    {
        var separator = name.IndexOf('@');
        return separator > 0 &&
            name.EndsWith(".service", StringComparison.Ordinal) &&
            TemplateFamilies.Contains(name[..separator]);
    }
}
