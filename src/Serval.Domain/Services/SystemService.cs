namespace Serval.Domain.Services;

/// <summary>
/// Read-only description of a discovered system-level systemd service.
/// </summary>
public sealed record SystemService
{
    public SystemService(
        SystemServiceId id,
        string description,
        SystemdLoadState loadState,
        SystemdActiveState activeState,
        SystemdSubState subState,
        bool isProtected = false)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(loadState);
        ArgumentNullException.ThrowIfNull(activeState);
        ArgumentNullException.ThrowIfNull(subState);

        Id = id;
        Description = description;
        LoadState = loadState;
        ActiveState = activeState;
        SubState = subState;
        IsProtected = isProtected;
    }

    public SystemServiceId Id { get; }

    public string Description { get; }

    public SystemdLoadState LoadState { get; }

    public SystemdActiveState ActiveState { get; }

    public SystemdSubState SubState { get; }

    /// <summary>
    /// Indicates that normal Serval permissions must not expose or manage this service.
    /// </summary>
    public bool IsProtected { get; }
}
