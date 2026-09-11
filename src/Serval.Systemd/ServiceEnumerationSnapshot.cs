using Serval.Domain.Services;

namespace Serval.Systemd;

// Internal discovery data, never an authorized application/IPC response.
internal sealed record ServiceEnumerationSnapshot(
    IReadOnlyList<EnumeratedSystemService> Services,
    IReadOnlyList<SystemServiceId> Templates);

internal sealed record EnumeratedSystemService(
    SystemService Service,
    IReadOnlyList<SystemServiceId> Names)
{
    internal bool IsProtected => BuiltInProtectedServices.IsProtected(Service.Id, Names);
}
