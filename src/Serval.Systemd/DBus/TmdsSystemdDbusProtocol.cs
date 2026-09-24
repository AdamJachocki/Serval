using Serval.Systemd.DBus.Generated;
using Tmds.DBus.Protocol;

namespace Serval.Systemd.DBus;

internal sealed class TmdsSystemdDbusProtocol : ISystemdDbusProtocol
{
    private const string SystemdDestination = "org.freedesktop.systemd1";
    private static readonly ObjectPath ManagerObjectPath = new("/org/freedesktop/systemd1");
    private readonly DBusConnection _connection;
    private readonly Manager _manager;
    private bool _disposed;

    private TmdsSystemdDbusProtocol(DBusConnection connection)
    {
        _connection = connection;
        _manager = new Manager(connection, SystemdDestination, ManagerObjectPath);
    }

    internal static async Task<TmdsSystemdDbusProtocol> ConnectAsync(
        CancellationToken cancellationToken)
    {
        var systemAddress = DBusAddress.System;
        if (systemAddress is null)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.Unavailable);
        }

        var connection = new DBusConnection(systemAddress);
        try
        {
            await InvokeAsync(
                    () => connection.ConnectAsync().AsTask(),
                    cancellationToken)
                .ConfigureAwait(false);
            return new TmdsSystemdDbusProtocol(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
        InvokeAsync(_manager.GetVersionAsync, cancellationToken);

    public async Task<ProtocolUnitFileEntry[]> ListUnitFilesAsync(
        CancellationToken cancellationToken)
    {
        var entries = await InvokeAsync(_manager.ListUnitFilesAsync, cancellationToken)
            .ConfigureAwait(false);
        return entries
            .Select(entry => new ProtocolUnitFileEntry(entry.Item1, entry.Item2))
            .ToArray();
    }

    public async Task<ProtocolListedUnit[]> ListUnitsByPatternsAsync(
        string[] states,
        string[] patterns,
        CancellationToken cancellationToken)
    {
        var entries = await InvokeAsync(
                () => _manager.ListUnitsByPatternsAsync(states, patterns),
                cancellationToken)
            .ConfigureAwait(false);
        return MapListedUnits(entries);
    }

    public async Task<ProtocolListedUnit[]> ListUnitsByNamesAsync(
        string[] names,
        CancellationToken cancellationToken)
    {
        var entries = await InvokeAsync(
                () => _manager.ListUnitsByNamesAsync(names),
                cancellationToken)
            .ConfigureAwait(false);
        return MapListedUnits(entries);
    }

    public async Task<ProtocolUnitProperties> ReadUnitPropertiesAsync(
        string objectPath,
        CancellationToken cancellationToken)
    {
        var unit = new Unit(_connection, SystemdDestination, new ObjectPath(objectPath));
        var id = await InvokeAsync(unit.GetIdAsync, cancellationToken).ConfigureAwait(false);
        var names = await InvokeAsync(unit.GetNamesAsync, cancellationToken).ConfigureAwait(false);
        var description = await InvokeAsync(unit.GetDescriptionAsync, cancellationToken)
            .ConfigureAwait(false);
        var loadState = await InvokeAsync(unit.GetLoadStateAsync, cancellationToken)
            .ConfigureAwait(false);
        var activeState = await InvokeAsync(unit.GetActiveStateAsync, cancellationToken)
            .ConfigureAwait(false);
        var subState = await InvokeAsync(unit.GetSubStateAsync, cancellationToken)
            .ConfigureAwait(false);

        return new ProtocolUnitProperties(
            id,
            names,
            description,
            loadState,
            activeState,
            subState);
    }

    public async Task<ProtocolEnvironmentProperties> ReadEnvironmentPropertiesAsync(
        string objectPath,
        CancellationToken cancellationToken)
    {
        var path = new ObjectPath(objectPath);
        var unit = new Unit(_connection, SystemdDestination, path);
        var service = new Service(_connection, SystemdDestination, path);
        var id = await InvokeAsync(unit.GetIdAsync, cancellationToken).ConfigureAwait(false);
        var names = await InvokeAsync(unit.GetNamesAsync, cancellationToken).ConfigureAwait(false);
        var loadState = await InvokeAsync(unit.GetLoadStateAsync, cancellationToken).ConfigureAwait(false);
        var fragmentPath = await InvokeAsync(unit.GetFragmentPathAsync, cancellationToken).ConfigureAwait(false);
        var dropInPaths = await InvokeAsync(unit.GetDropInPathsAsync, cancellationToken).ConfigureAwait(false);
        var needDaemonReload = await InvokeAsync(unit.GetNeedDaemonReloadAsync, cancellationToken).ConfigureAwait(false);
        var transient = await InvokeAsync(unit.GetTransientAsync, cancellationToken).ConfigureAwait(false);
        var unitFileState = await InvokeAsync(unit.GetUnitFileStateAsync, cancellationToken).ConfigureAwait(false);
        var environment = await InvokeAsync(service.GetEnvironmentAsync, cancellationToken).ConfigureAwait(false);
        var environmentFiles = await InvokeAsync(service.GetEnvironmentFilesAsync, cancellationToken).ConfigureAwait(false);
        var unsetEnvironment = await InvokeAsync(service.GetUnsetEnvironmentAsync, cancellationToken).ConfigureAwait(false);
        var passEnvironment = await InvokeAsync(service.GetPassEnvironmentAsync, cancellationToken).ConfigureAwait(false);

        return new ProtocolEnvironmentProperties(
            id,
            names,
            loadState,
            fragmentPath,
            dropInPaths,
            needDaemonReload,
            transient,
            unitFileState,
            environment,
            environmentFiles.Select(entry => new ProtocolEnvironmentFile(entry.Item1, entry.Item2)).ToArray(),
            unsetEnvironment,
            passEnvironment);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _connection.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static ProtocolListedUnit[] MapListedUnits(
        (
            string,
            string,
            string,
            string,
            string,
            string,
            ObjectPath,
            uint,
            string,
            ObjectPath)[] entries) =>
        entries
            .Select(entry => new ProtocolListedUnit(
                entry.Item1,
                entry.Item2,
                entry.Item3,
                entry.Item4,
                entry.Item5,
                entry.Item6,
                entry.Item7.ToString()))
            .ToArray();

    internal static SystemdDbusProtocolException MapReadException(DBusReadException _)
        => new(SystemdDbusProtocolFailureKind.IncompatibleReply);

    private static async Task InvokeAsync(
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DBusErrorReplyException exception)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.RemoteError,
                exception.ErrorName);
        }
        catch (DBusReplyLimitException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.LimitExceeded);
        }
        catch (DBusUnexpectedValueException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.IncompatibleReply);
        }
        catch (DBusReadException exception)
        {
            throw MapReadException(exception);
        }
        catch (DBusConnectionException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.Unavailable);
        }
    }

    private static async Task<T> InvokeAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DBusErrorReplyException exception)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.RemoteError,
                exception.ErrorName);
        }
        catch (DBusReplyLimitException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.LimitExceeded);
        }
        catch (DBusUnexpectedValueException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.IncompatibleReply);
        }
        catch (DBusReadException exception)
        {
            throw MapReadException(exception);
        }
        catch (DBusConnectionException)
        {
            throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.Unavailable);
        }
    }
}
