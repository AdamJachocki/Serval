namespace Serval.Systemd.DBus;

internal interface ISystemdDbusProtocol : IAsyncDisposable
{
    Task<string> GetManagerVersionAsync(CancellationToken cancellationToken);

    Task<ProtocolUnitFileEntry[]> ListUnitFilesAsync(CancellationToken cancellationToken);

    Task<ProtocolListedUnit[]> ListUnitsByPatternsAsync(
        string[] states,
        string[] patterns,
        CancellationToken cancellationToken);

    Task<ProtocolListedUnit[]> ListUnitsByNamesAsync(
        string[] names,
        CancellationToken cancellationToken);

    Task<ProtocolUnitProperties> ReadUnitPropertiesAsync(
        string objectPath,
        CancellationToken cancellationToken);

    Task<ProtocolEnvironmentProperties> ReadEnvironmentPropertiesAsync(
        string objectPath,
        CancellationToken cancellationToken);
}

internal sealed record ProtocolUnitFileEntry(string Path, string State);

internal sealed record ProtocolListedUnit(
    string Name,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState,
    string FollowedUnit,
    string ObjectPath);

internal sealed record ProtocolUnitProperties(
    string Id,
    string[] Names,
    string Description,
    string LoadState,
    string ActiveState,
    string SubState);

internal sealed record ProtocolEnvironmentFile(string Path, bool IgnoreErrors);

internal sealed record ProtocolEnvironmentProperties(
    string Id,
    string[] Names,
    string LoadState,
    string FragmentPath,
    string[] DropInPaths,
    bool NeedDaemonReload,
    bool Transient,
    string UnitFileState,
    string[] Environment,
    ProtocolEnvironmentFile[] EnvironmentFiles,
    string[] UnsetEnvironment,
    string[] PassEnvironment);

internal enum SystemdDbusProtocolFailureKind
{
    Unavailable,
    RemoteError,
    IncompatibleReply,
    LimitExceeded,
}

internal sealed class SystemdDbusProtocolException : Exception
{
    internal SystemdDbusProtocolException(
        SystemdDbusProtocolFailureKind failureKind,
        string? remoteErrorName = null)
        : base("The D-Bus protocol operation failed.")
    {
        FailureKind = failureKind;
        RemoteErrorName = remoteErrorName;
    }

    public SystemdDbusProtocolFailureKind FailureKind { get; }

    public string? RemoteErrorName { get; }
}
