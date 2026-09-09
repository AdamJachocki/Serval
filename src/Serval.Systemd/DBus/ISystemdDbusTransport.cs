using Serval.Domain.Services;

namespace Serval.Systemd.DBus;

internal interface ISystemdDbusTransport : IAsyncDisposable
{
    Task<string> GetManagerVersionAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
        IReadOnlyCollection<SystemServiceId> serviceIds,
        CancellationToken cancellationToken);

    Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
        SystemdUnitReference unit,
        CancellationToken cancellationToken);
}

internal sealed record SystemdUnitFileEntry
{
    public SystemdUnitFileEntry(string path, string state)
    {
        Path = path;
        State = state;
    }

    public string Path { get; }

    public string State { get; }
}

internal sealed record SystemdListedUnit
{
    public SystemdListedUnit(
        string name,
        string description,
        string loadState,
        string activeState,
        string subState,
        string followedUnit,
        SystemdUnitReference unit)
    {
        Name = name;
        Description = description;
        LoadState = loadState;
        ActiveState = activeState;
        SubState = subState;
        FollowedUnit = followedUnit;
        Unit = unit;
    }

    public string Name { get; }

    public string Description { get; }

    public string LoadState { get; }

    public string ActiveState { get; }

    public string SubState { get; }

    public string FollowedUnit { get; }

    public SystemdUnitReference Unit { get; }
}

internal sealed record SystemdUnitProperties
{
    public SystemdUnitProperties(
        string id,
        IReadOnlyList<string> names,
        string description,
        string loadState,
        string activeState,
        string subState)
    {
        Id = id;
        Names = names;
        Description = description;
        LoadState = loadState;
        ActiveState = activeState;
        SubState = subState;
    }

    public string Id { get; }

    public IReadOnlyList<string> Names { get; }

    public string Description { get; }

    public string LoadState { get; }

    public string ActiveState { get; }

    public string SubState { get; }
}

internal sealed class SystemdUnitReference
{
    internal SystemdUnitReference()
    {
    }
}
