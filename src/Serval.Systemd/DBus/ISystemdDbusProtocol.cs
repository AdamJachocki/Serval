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

internal enum SystemdDbusProtocolFailureKind
{
    Unavailable,
    RemoteError,
    IncompatibleReply,
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
