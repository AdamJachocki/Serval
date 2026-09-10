using Serval.Domain.Services;

namespace Serval.Systemd;

// Internal metadata only. Agent must authorize the canonical Id and all Names before exposure.
internal abstract record SystemdServiceInspectionResult
{
    private SystemdServiceInspectionResult() { }

    internal sealed record Found(SystemService Service, IReadOnlyList<SystemServiceId> Names)
        : SystemdServiceInspectionResult;

    internal sealed record NotFound(SystemServiceId ServiceId) : SystemdServiceInspectionResult;
}
