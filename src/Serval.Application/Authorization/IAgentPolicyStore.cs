namespace Serval.Application.Authorization;

public sealed record StoredGrant(
    long Id, string SubjectKind, string SubjectName, uint SubjectId,
    string Operation, string Service);

public sealed record AuditEntry(
    string? ActorName, uint? ActorUid, string Operation, string? Service,
    string Outcome, string CorrelationId, DateTimeOffset Timestamp);

/// <summary>Agent-facing grant and audit operations; persistence details stay in Infrastructure.</summary>
public interface IAgentPolicyStore
{
    IReadOnlyList<StoredGrant> ListGrants();

    long AddGrant(StoredGrant grant, AuditEntry audit);

    bool RemoveGrant(long id, AuditEntry audit);

    void AppendAudit(AuditEntry audit);
}
