using Serval.Domain.Services;

namespace Serval.Systemd;

/// <summary>Agent-facing policy over validated system service identities.</summary>
public static class SystemdProtectedServicePolicy
{
    public static bool IsProtected(SystemServiceId canonicalId, IReadOnlyList<SystemServiceId> names) =>
        BuiltInProtectedServices.IsProtected(canonicalId, names);
}
