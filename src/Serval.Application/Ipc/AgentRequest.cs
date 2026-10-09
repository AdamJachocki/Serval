namespace Serval.Application.Ipc;

/// <summary>A request accepted by the privileged Agent. No caller-authored identity is carried.</summary>
public abstract record AgentRequest(string CorrelationId)
{
    public sealed record Login(string CorrelationId, string Username, string Password)
        : AgentRequest(CorrelationId);

    public sealed record Logout(string CorrelationId, string Session)
        : AgentRequest(CorrelationId);

    public sealed record ListServices(string CorrelationId, string Session)
        : AgentRequest(CorrelationId);

    public sealed record InspectService(string CorrelationId, string Session, string Service)
        : AgentRequest(CorrelationId);

    public sealed record ListGrants(string CorrelationId, string Session)
        : AgentRequest(CorrelationId);

    public sealed record AddGrant(
        string CorrelationId, string Session, string SubjectKind, string Subject,
        string Operation, string Service) : AgentRequest(CorrelationId);

    public sealed record RemoveGrant(string CorrelationId, string Session, long GrantId)
        : AgentRequest(CorrelationId);
}

public enum AgentResultCode
{
    Success,
    InvalidRequest,
    Unauthenticated,
    Denied,
    NotFound,
    Unavailable,
}

public sealed record AgentService(
    string Id, string Description, string LoadState, string ActiveState, string SubState);

public sealed record AgentGrant(
    long Id, string SubjectKind, string Subject, string Operation, string Service);

/// <summary>Only operation-specific, non-secret results are exposed to Web.</summary>
public abstract record AgentResult(AgentResultCode Code)
{
    public sealed record Login(AgentResultCode Code, string? Session) : AgentResult(Code);

    public sealed record Logout(AgentResultCode Code) : AgentResult(Code);

    public sealed record ListServices(AgentResultCode Code, IReadOnlyList<AgentService>? Services)
        : AgentResult(Code);

    public sealed record InspectService(AgentResultCode Code, AgentService? Service)
        : AgentResult(Code);

    public sealed record ListGrants(AgentResultCode Code, IReadOnlyList<AgentGrant>? Grants)
        : AgentResult(Code);

    public sealed record AddGrant(AgentResultCode Code, long? GrantId) : AgentResult(Code);

    public sealed record RemoveGrant(AgentResultCode Code) : AgentResult(Code);
}
