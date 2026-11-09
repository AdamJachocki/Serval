namespace Serval.Application.Authorization;

public enum FavoriteAddOutcome
{
    Added,
    AlreadyPresent,
    LimitReached,
}

/// <summary>
/// Agent-facing per-user favorite services. Favorites are keyed by the verified
/// user name and UID; callers enforce service visibility and protected policy.
/// </summary>
public interface IAgentFavoriteStore
{
    /// <summary>Returns the stored canonical service IDs for one verified identity.</summary>
    IReadOnlyList<string> ListFavorites(string name, uint uid);

    /// <summary>
    /// Adds one favorite and its audit row in a single transaction. The audit outcome is
    /// derived from the result (<c>Success</c> or <c>LimitReached</c>); an existing favorite
    /// is not counted again, so re-adding at the limit succeeds.
    /// </summary>
    FavoriteAddOutcome TryAdd(string name, uint uid, string service, int limit, AuditEntry audit);

    /// <summary>Removes only the identity's own favorite, if any, and writes the audit row in the same transaction.</summary>
    void Remove(string name, uint uid, string service, AuditEntry audit);
}
