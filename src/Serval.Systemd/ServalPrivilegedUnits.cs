using Serval.Domain.Services;

namespace Serval.Systemd;

// Mandatory discovery exclusion; ordinary application input cannot alter this policy.
internal static class ServalPrivilegedUnits
{
    private const string AgentService = "serval-agent.service";
    private const string AgentTemplateFamily = "serval-agent";

    internal static bool IsExcluded(SystemServiceId id) =>
        string.Equals(id.Value, AgentService, StringComparison.Ordinal) ||
        IsAgentTemplateFamily(id.Value);

    internal static bool IsExcluded(
        SystemServiceId canonicalId, IReadOnlyList<SystemServiceId> names) =>
        IsExcluded(canonicalId) || names.Any(IsExcluded);

    private static bool IsAgentTemplateFamily(string name)
    {
        var separator = name.IndexOf('@');
        return separator > 0 &&
            name.EndsWith(".service", StringComparison.Ordinal) &&
            string.Equals(name[..separator], AgentTemplateFamily, StringComparison.Ordinal);
    }
}
