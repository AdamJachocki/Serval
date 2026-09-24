using System.Collections.Concurrent;
using Serval.Domain.Services;

namespace Serval.Systemd.DBus;

internal sealed class SystemdDbusTransport : ISystemdDbusTransport
{
    private const int MaximumBatchSize = 65_536;
    private const int MaximumDescriptionLength = 16_384;
    private const int MaximumObjectPathLength = 4_096;
    private const int MaximumPathLength = 4_096;
    private const int MaximumStateLength = 128;
    private const int MaximumVersionLength = 256;
    private const string ServiceUnitPattern = "*.service";
    private const string UnitObjectPathPrefix = "/org/freedesktop/systemd1/unit/";
    private static readonly TimeSpan MaximumDeadline = TimeSpan.FromMinutes(2);
    private readonly CancellationTokenSource _deadlineCancellation;
    private readonly ConcurrentDictionary<SystemdUnitReference, string> _issuedUnits = new();
    private readonly ConcurrentDictionary<string, SystemdUnitReference> _unitReferences = new(StringComparer.Ordinal);
    private readonly ISystemdDbusProtocol _protocol;
    private bool _disposed;

    internal SystemdDbusTransport(
        ISystemdDbusProtocol protocol,
        TimeSpan deadline,
        TimeProvider? timeProvider = null)
        : this(
            protocol,
            CreateDeadlineCancellation(deadline, timeProvider ?? TimeProvider.System))
    {
    }

    private SystemdDbusTransport(
        ISystemdDbusProtocol protocol,
        CancellationTokenSource deadlineCancellation)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _protocol = protocol;
        _deadlineCancellation = deadlineCancellation;
    }

    internal static Task<SystemdDbusTransport> ConnectAsync(
        TimeSpan deadline,
        CancellationToken cancellationToken) =>
        ConnectAsync(deadline, TimeProvider.System, cancellationToken);

    internal static async Task<SystemdDbusTransport> ConnectAsync(
        TimeSpan deadline,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var deadlineCancellation = CreateDeadlineCancellation(deadline, timeProvider);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadlineCancellation.Token);

        try
        {
            var protocol = await TmdsSystemdDbusProtocol.ConnectAsync(
                    linkedCancellation.Token)
                .ConfigureAwait(false);
            return new SystemdDbusTransport(protocol, deadlineCancellation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            deadlineCancellation.Dispose();
            throw;
        }
        catch (OperationCanceledException) when (deadlineCancellation.IsCancellationRequested)
        {
            deadlineCancellation.Dispose();
            throw new SystemdDbusException(SystemdDbusFailureKind.Timeout);
        }
        catch (SystemdDbusProtocolException exception)
        {
            deadlineCancellation.Dispose();
            throw MapProtocolException(exception);
        }
    }

    public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            async token =>
            {
                var version = await _protocol.GetManagerVersionAsync(token).ConfigureAwait(false);
                return ValidateString(version, MaximumVersionLength, allowEmpty: false);
            },
            cancellationToken);

    public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            async token =>
            {
                var entries = await _protocol.ListUnitFilesAsync(token).ConfigureAwait(false);
                ValidateArray(entries);

                return (IReadOnlyList<SystemdUnitFileEntry>)entries
                    .Select(entry =>
                    {
                        EnsureReplyValue(entry);
                        return new SystemdUnitFileEntry(
                            ValidateString(entry.Path, MaximumPathLength, allowEmpty: false),
                            ValidateString(entry.State, MaximumStateLength, allowEmpty: false));
                    })
                    .ToArray();
            },
            cancellationToken);

    public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            async token =>
            {
                var entries = await _protocol.ListUnitsByPatternsAsync(
                        [],
                        [ServiceUnitPattern],
                        token)
                    .ConfigureAwait(false);
                return (IReadOnlyList<SystemdListedUnit>)MapListedUnits(entries);
            },
            cancellationToken);

    public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
        IReadOnlyCollection<SystemServiceId> serviceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        if (serviceIds.Count > MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceIds),
                "The service identifier batch is too large.");
        }

        var names = serviceIds
            .Select(serviceId =>
            {
                ArgumentNullException.ThrowIfNull(serviceId);
                return serviceId.Value;
            })
            .ToArray();

        return ExecuteAsync(
            async token =>
            {
                var entries = await _protocol.ListUnitsByNamesAsync(names, token)
                    .ConfigureAwait(false);
                return (IReadOnlyList<SystemdListedUnit>)MapListedUnits(entries);
            },
            cancellationToken);
    }

    public Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
        SystemdUnitReference unit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_issuedUnits.TryGetValue(unit, out var objectPath))
        {
            throw new ArgumentException(
                "The unit reference was not issued by this transport.",
                nameof(unit));
        }

        return ExecuteAsync(
            async token =>
            {
                var properties = await _protocol.ReadUnitPropertiesAsync(
                        objectPath,
                        token)
                    .ConfigureAwait(false);
                EnsureReplyValue(properties);
                ValidateArray(properties.Names);
                if (properties.Names.Length == 0)
                {
                    throw new MalformedReplyException();
                }

                return new SystemdUnitProperties(
                    ValidateUnitName(properties.Id),
                    properties.Names.Select(ValidateUnitName).ToArray(),
                    ValidateString(
                        properties.Description,
                        MaximumDescriptionLength,
                        allowEmpty: true),
                    ValidateString(properties.LoadState, MaximumStateLength, allowEmpty: false),
                    ValidateString(properties.ActiveState, MaximumStateLength, allowEmpty: false),
                    ValidateString(properties.SubState, MaximumStateLength, allowEmpty: false));
            },
            cancellationToken);
    }

    public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
        SystemdUnitReference unit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (!_issuedUnits.TryGetValue(unit, out var objectPath))
            throw new ArgumentException("The unit reference was not issued by this transport.", nameof(unit));

        return ExecuteAsync(
            async token =>
            {
                var properties = await _protocol.ReadEnvironmentPropertiesAsync(objectPath, token)
                    .ConfigureAwait(false);
                EnsureReplyValue(properties);
                ValidateArray(properties.Names);
                ValidateArray(properties.DropInPaths, EnvironmentReadLimits.MaxConfigurationPaths);
                ValidateArray(properties.Environment, EnvironmentReadLimits.MaxAssignments + 1);
                ValidateArray(properties.EnvironmentFiles, EnvironmentReadLimits.MaxSources);
                ValidateArray(properties.UnsetEnvironment, EnvironmentReadLimits.MaxAssignments);
                ValidateArray(properties.PassEnvironment, EnvironmentReadLimits.MaxAssignments);
                if (properties.Names.Length == 0)
                    throw new MalformedReplyException();

                return new SystemdEnvironmentProperties(
                    ValidateUnitName(properties.Id),
                    Array.AsReadOnly(properties.Names.Select(ValidateUnitName).ToArray()),
                    ValidateString(properties.LoadState, MaximumStateLength, allowEmpty: false),
                    ValidateString(properties.FragmentPath, MaximumPathLength + 1, allowEmpty: true),
                    Array.AsReadOnly(properties.DropInPaths
                        .Select(path => ValidateString(path, MaximumPathLength + 1, allowEmpty: false)).ToArray()),
                    properties.NeedDaemonReload,
                    properties.Transient,
                    ValidateString(properties.UnitFileState, MaximumStateLength, allowEmpty: false),
                    Array.AsReadOnly(properties.Environment
                        .Select(entry => ValidateString(entry, EnvironmentReadLimits.MaxLogicalRecordBytes + 1, allowEmpty: false))
                        .ToArray()),
                    Array.AsReadOnly(properties.EnvironmentFiles.Select(file =>
                    {
                        EnsureReplyValue(file);
                        return new SystemdEnvironmentFile(
                            ValidateString(file.Path, MaximumPathLength + 1, allowEmpty: false),
                            file.IgnoreErrors);
                    }).ToArray()),
                    Array.AsReadOnly(properties.UnsetEnvironment
                        .Select(entry => ValidateString(entry, EnvironmentReadLimits.MaxLogicalRecordBytes, allowEmpty: false))
                        .ToArray()),
                    Array.AsReadOnly(properties.PassEnvironment
                        .Select(entry => ValidateString(entry, EnvironmentReadLimits.MaxLogicalRecordBytes, allowEmpty: false))
                        .ToArray()));
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _deadlineCancellation.CancelAsync().ConfigureAwait(false);
        _deadlineCancellation.Dispose();
        await _protocol.DisposeAsync().ConfigureAwait(false);
    }

    private static CancellationTokenSource CreateDeadlineCancellation(
        TimeSpan deadline,
        TimeProvider timeProvider)
    {
        if (deadline <= TimeSpan.Zero || deadline > MaximumDeadline)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                $"The deadline must be positive and no greater than {MaximumDeadline}.");
        }

        return new CancellationTokenSource(deadline, timeProvider);
    }

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _deadlineCancellation.Token);

        try
        {
            return await operation(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await AbortAsync().ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (_deadlineCancellation.IsCancellationRequested)
        {
            await AbortAsync().ConfigureAwait(false);
            throw new SystemdDbusException(SystemdDbusFailureKind.Timeout);
        }
        catch (SystemdDbusProtocolException exception)
        {
            throw MapProtocolException(exception);
        }
        catch (MalformedReplyException)
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }
    }

    private async ValueTask AbortAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deadlineCancellation.Dispose();
        await _protocol.DisposeAsync().ConfigureAwait(false);
    }

    private SystemdListedUnit[] MapListedUnits(ProtocolListedUnit[] entries)
    {
        ValidateArray(entries);

        return entries
            .Select(entry =>
            {
                EnsureReplyValue(entry);
                var objectPath = ValidateObjectPath(entry.ObjectPath);
                var unit = _unitReferences.GetOrAdd(objectPath, _ => new SystemdUnitReference());
                _issuedUnits[unit] = objectPath;
                return new SystemdListedUnit(
                    ValidateUnitName(entry.Name),
                    ValidateString(
                        entry.Description,
                        MaximumDescriptionLength,
                        allowEmpty: true),
                    ValidateString(entry.LoadState, MaximumStateLength, allowEmpty: false),
                    ValidateString(entry.ActiveState, MaximumStateLength, allowEmpty: false),
                    ValidateString(entry.SubState, MaximumStateLength, allowEmpty: false),
                    ValidateOptionalUnitName(entry.FollowedUnit),
                    unit);
            })
            .ToArray();
    }

    private static string ValidateObjectPath(string value)
    {
        var objectPath = ValidateString(value, MaximumObjectPathLength, allowEmpty: false);
        if (!objectPath.StartsWith(UnitObjectPathPrefix, StringComparison.Ordinal))
        {
            throw new MalformedReplyException();
        }

        var suffix = objectPath.AsSpan(UnitObjectPathPrefix.Length);
        if (suffix.IsEmpty)
        {
            throw new MalformedReplyException();
        }

        foreach (var character in suffix)
        {
            if (character is not (>= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or
                '_'))
            {
                throw new MalformedReplyException();
            }
        }

        return objectPath;
    }

    private static string ValidateOptionalUnitName(string value) =>
        string.IsNullOrEmpty(value)
            ? ValidateString(value, 255, allowEmpty: true)
            : ValidateUnitName(value);

    private static string ValidateUnitName(string value)
    {
        try
        {
            return new SystemServiceId(value).Value;
        }
        catch (ArgumentException)
        {
            throw new MalformedReplyException();
        }
    }

    private static string ValidateString(string? value, int maximumLength, bool allowEmpty)
    {
        if (value is null ||
            value.Length > maximumLength ||
            (!allowEmpty && value.Length == 0) ||
            value.Contains('\0'))
        {
            throw new MalformedReplyException();
        }

        return value;
    }

    private static void ValidateArray<T>(T[]? entries) => ValidateArray(entries, MaximumBatchSize);

    private static void ValidateArray<T>(T[]? entries, int maximumCount)
    {
        if (entries is null || entries.Length > maximumCount)
        {
            throw new MalformedReplyException();
        }
    }

    private static void EnsureReplyValue<T>(T? value)
        where T : class
    {
        if (value is null)
        {
            throw new MalformedReplyException();
        }
    }

    private static SystemdDbusException MapProtocolException(
        SystemdDbusProtocolException exception) =>
        exception.FailureKind switch
        {
            SystemdDbusProtocolFailureKind.Unavailable =>
                new SystemdDbusException(SystemdDbusFailureKind.Unavailable),
            SystemdDbusProtocolFailureKind.RemoteError =>
                new SystemdDbusException(
                    SystemdDbusFailureKind.RemoteError,
                    SanitizeRemoteErrorName(exception.RemoteErrorName)),
            SystemdDbusProtocolFailureKind.IncompatibleReply =>
                new SystemdDbusException(SystemdDbusFailureKind.IncompatibleReply),
            SystemdDbusProtocolFailureKind.LimitExceeded =>
                new SystemdDbusException(SystemdDbusFailureKind.LimitExceeded),
            _ =>
                new SystemdDbusException(SystemdDbusFailureKind.Unavailable),
        };

    private static string? SanitizeRemoteErrorName(string? errorName)
    {
        if (string.IsNullOrEmpty(errorName) || errorName.Length > 255)
        {
            return null;
        }

        return errorName.All(character =>
            character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or
                >= '0' and <= '9' or
                '.' or '_')
            ? errorName
            : null;
    }

    private sealed class MalformedReplyException : Exception;
}
