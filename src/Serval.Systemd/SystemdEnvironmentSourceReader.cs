using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using static Serval.Systemd.SystemdServiceIdentity;

namespace Serval.Systemd;

internal delegate Task<ISystemdDbusTransport> SystemdEnvironmentTransportFactory(
    TimeSpan remainingAllowance, CancellationToken cancellationToken);

internal enum SystemdEnvironmentReadStage
{
    BeforeConnection,
    BeforeResolution,
    BeforeEnvironmentAcquisition,
    BeforeFileObservation,
    BeforeParsing,
    BeforeComposition,
    BeforeFinalValidation,
    BeforeCleanup,
    BeforePublication,
}

// Internal acquisition only. Agent authorization and application composition remain outside this type.
internal sealed class SystemdEnvironmentSourceReader
{
    private readonly SystemdEnvironmentTransportFactory _connect;
    private readonly ISystemdSourceFileAccess _files;
    private readonly TimeProvider _timeProvider;
    private readonly Action<SystemdEnvironmentReadStage>? _stageObserver;

    internal SystemdEnvironmentSourceReader()
        : this(
            static async (allowance, token) => await SystemdDbusTransport.ConnectAsync(
                allowance, token).ConfigureAwait(false),
            new LinuxSystemdSourceFileAccess(), TimeProvider.System)
    {
    }

    internal SystemdEnvironmentSourceReader(
        Func<CancellationToken, Task<ISystemdDbusTransport>> connect,
        ISystemdSourceFileAccess files,
        TimeProvider timeProvider)
        : this((_, token) => connect(token), files, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connect);
    }

    internal SystemdEnvironmentSourceReader(
        SystemdEnvironmentTransportFactory connect,
        ISystemdSourceFileAccess files,
        TimeProvider timeProvider,
        Action<SystemdEnvironmentReadStage>? stageObserver = null)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _stageObserver = stageObserver;
    }

    /// <summary>
    /// Compatibility acquisition path. It uses the same policy and retained-session pipeline as
    /// the application reader, but validates and closes the session before returning raw sources.
    /// </summary>
    internal async Task<SystemdEnvironmentSourceReadResult> ReadAsync(
        SystemServiceId serviceId, CancellationToken cancellationToken)
    {
        ValidateRequest(serviceId);
        using var operation = new SystemdEnvironmentOperationContext(_timeProvider, cancellationToken);
        try
        {
            var acquisition = await AcquireAsync(serviceId, operation).ConfigureAwait(false);
            if (acquisition.Failure is { } acquisitionFailure)
                return operation.Prefer(acquisitionFailure);

            await using var session = acquisition.Session!;
            SystemdEnvironmentSourceReadResult.Success? snapshot = null;
            var transferred = false;
            try
            {
                snapshot = session.TakeSnapshot();
                var validationFailure = await session.ValidateAsync(operation).ConfigureAwait(false);
                if (validationFailure is not null)
                    return operation.Prefer(validationFailure);

                var cleanupFailure = await session.CloseAsync(operation).ConfigureAwait(false);
                if (cleanupFailure is not null)
                    return operation.Prefer(cleanupFailure);

                operation.ThrowIfStopped();
                transferred = true;
                return snapshot;
            }
            finally
            {
                if (!transferred)
                    snapshot?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (operation.HasExpired)
        {
            return operation.Prefer(Failure(EnvironmentReadFailureCode.Timeout));
        }
    }

    internal async Task<SystemdEnvironmentAcquisition> AcquireAsync(
        SystemServiceId serviceId, SystemdEnvironmentOperationContext operation)
    {
        ValidateRequest(serviceId);
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfStopped();
        if (BuiltInProtectedServices.IsProtected(serviceId))
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(Failure(EnvironmentReadFailureCode.ProtectedTarget)));

        ISystemdDbusTransport? transport = null;
        LoadedEnvironmentResult.Success? manager = null;
        var configuration = new List<SystemdFileObservation>();
        var files = new List<SystemdFileObservation>();
        var transferred = false;
        try
        {
            ObserveStage(SystemdEnvironmentReadStage.BeforeConnection, operation);
            transport = await _connect(operation.RemainingAllowance, operation.Token)
                .ConfigureAwait(false);
            operation.ThrowIfStopped();
            SystemdCompatibility.ValidateVersion(
                await transport.GetManagerVersionAsync(operation.Token).ConfigureAwait(false));

            ObserveStage(SystemdEnvironmentReadStage.BeforeResolution, operation);
            var resolved = await SystemdServiceResolver.ResolveOnceAsync(
                    transport, serviceId, operation.Token)
                .ConfigureAwait(false);
            operation.ThrowIfStopped();
            if (resolved is null)
                return SystemdEnvironmentAcquisition.FromFailure(
                    operation.Prefer(Failure(EnvironmentReadFailureCode.NotFound)));
            if (BuiltInProtectedServices.IsProtected(resolved.Identity.Id, resolved.Identity.Names))
                return SystemdEnvironmentAcquisition.FromFailure(
                    operation.Prefer(Failure(EnvironmentReadFailureCode.ProtectedTarget)));

            ObserveStage(SystemdEnvironmentReadStage.BeforeEnvironmentAcquisition, operation);
            SystemdEnvironmentProperties initial;
            try
            {
                initial = await transport.ReadEnvironmentPropertiesAsync(resolved.Unit, operation.Token)
                    .ConfigureAwait(false);
            }
            catch (SystemdDbusException exception) when (IsDisappearance(exception))
            {
                return SystemdEnvironmentAcquisition.FromFailure(
                    operation.Prefer(Failure(EnvironmentReadFailureCode.InconsistentSnapshot)));
            }
            catch (SystemdDbusException exception) when (IsMissingRequiredProperty(exception))
            {
                return SystemdEnvironmentAcquisition.FromFailure(
                    operation.Prefer(Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty)));
            }

            var validationFailure = ValidateInitial(initial, resolved.Identity, serviceId);
            if (validationFailure is not null)
                return SystemdEnvironmentAcquisition.FromFailure(operation.Prefer(validationFailure));

            foreach (var path in ConfigurationPaths(initial))
            {
                ObserveStage(SystemdEnvironmentReadStage.BeforeFileObservation, operation);
                var observation = await _files.ObserveAsync(
                    path, readContent: false, allowMissing: false, operation.Token)
                    .ConfigureAwait(false);
                operation.ThrowIfStopped();
                configuration.Add(observation);
            }

            var decoded = LoadedEnvironmentDecoder.Decode(initial.Environment.ToArray(), operation.Token);
            if (decoded is LoadedEnvironmentResult.Failure decodeFailure)
                return SystemdEnvironmentAcquisition.FromFailure(operation.Prefer(
                    Failure(decodeFailure.Code, sourceId: 0)));

            manager = (LoadedEnvironmentResult.Success)decoded;
            var totalBytes = manager.SourceBytes;
            for (var index = 0; index < initial.EnvironmentFiles.Count; index++)
            {
                ObserveStage(SystemdEnvironmentReadStage.BeforeFileObservation, operation);
                var declaration = initial.EnvironmentFiles[index];
                var sourceId = index + 1;
                SystemdFileObservation observation;
                try
                {
                    var remainingBytes = EnvironmentReadLimits.MaxTotalSourceBytes - totalBytes;
                    observation = await _files.ObserveAsync(
                        declaration.Path, readContent: true,
                        allowMissing: declaration.IgnoreErrors, operation.Token,
                        Math.Min(EnvironmentReadLimits.MaxSourceBytes, remainingBytes))
                        .ConfigureAwait(false);
                    operation.ThrowIfStopped();
                }
                catch (SystemdSourceFileException exception)
                {
                    return SystemdEnvironmentAcquisition.FromFailure(
                        operation.Prefer(MapFileFailure(exception.Failure, sourceId)));
                }

                files.Add(observation);
                if (!observation.IsMissing)
                {
                    if (observation.ContentLength > EnvironmentReadLimits.MaxTotalSourceBytes - totalBytes)
                    {
                        return SystemdEnvironmentAcquisition.FromFailure(operation.Prefer(
                            Failure(EnvironmentReadFailureCode.LimitExceeded, sourceId: sourceId)));
                    }
                    totalBytes += observation.ContentLength;
                }
            }

            var session = new SystemdEnvironmentAcquisitionSession(
                transport, _files, resolved, serviceId, initial, manager,
                configuration, files, _stageObserver);
            transport = null;
            manager = null;
            configuration = [];
            files = [];
            transferred = true;
            return SystemdEnvironmentAcquisition.FromSession(session);
        }
        catch (OperationCanceledException) when (operation.CallerCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CallerCancellation);
        }
        catch (OperationCanceledException) when (operation.HasExpired)
        {
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(Failure(EnvironmentReadFailureCode.Timeout)));
        }
        catch (SystemdSourceFileException exception)
        {
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(MapFileFailure(exception.Failure, sourceId: null)));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.Timeout && operation.HasExpired)
        {
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(Failure(EnvironmentReadFailureCode.Timeout)));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.LimitExceeded)
        {
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(Failure(EnvironmentReadFailureCode.LimitExceeded)));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind is SystemdDbusFailureKind.MalformedReply or
                SystemdDbusFailureKind.IncompatibleReply or
                SystemdDbusFailureKind.UnsupportedVersion)
        {
            return SystemdEnvironmentAcquisition.FromFailure(operation.Prefer(Failure(
                EnvironmentReadFailureCode.UnsupportedConfiguration,
                EnvironmentUnsupportedReason.UnsupportedProperty)));
        }
        catch (SystemdDbusException)
        {
            return SystemdEnvironmentAcquisition.FromFailure(
                operation.Prefer(Failure(EnvironmentReadFailureCode.TransportError)));
        }
        finally
        {
            if (!transferred)
            {
                for (var index = files.Count - 1; index >= 0; index--)
                    files[index].Dispose();
                for (var index = configuration.Count - 1; index >= 0; index--)
                    configuration[index].Dispose();
                manager?.Dispose();
                if (transport is not null)
                {
                    try
                    {
                        await transport.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsExpectedCleanupFailure(exception))
                    {
                        // A controlled acquisition failure remains primary and contains no raw detail.
                    }
                }
            }
        }
    }

    private void ObserveStage(
        SystemdEnvironmentReadStage stage, SystemdEnvironmentOperationContext operation)
    {
        operation.ThrowIfStopped();
        _stageObserver?.Invoke(stage);
        operation.ThrowIfStopped();
    }

    private static void ValidateRequest(SystemServiceId serviceId)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        _ = new SystemServiceId(serviceId.Value);
        if (IsTemplate(serviceId.Value))
            throw new ArgumentException(
                "Environment reading requires a concrete service identifier.", nameof(serviceId));
    }

    internal static SystemdEnvironmentSourceReadResult.Failure? ValidateInitial(
        SystemdEnvironmentProperties properties,
        SystemdServiceIdentity resolved,
        SystemServiceId requested)
    {
        var identity = new SystemdServiceIdentity(new SystemdUnitProperties(
            properties.Id, properties.Names, string.Empty, properties.LoadState,
            "inactive", "dead"));
        if (!string.Equals(identity.Id.Value, resolved.Id.Value, StringComparison.Ordinal))
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        identity.RequireName(requested.Value);
        identity.RequireName(resolved.Id.Value);
        if (!identity.Names.SequenceEqual(resolved.Names))
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        if (properties.LoadState == "not-found")
            return Failure(EnvironmentReadFailureCode.NotFound);
        if (!string.Equals(properties.LoadState, "loaded", StringComparison.Ordinal))
            return Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty);
        if (properties.NeedDaemonReload)
            return Failure(EnvironmentReadFailureCode.InconsistentSnapshot);
        if (properties.Transient)
            return Unsupported(EnvironmentUnsupportedReason.TransientUnit);
        if (string.Equals(properties.UnitFileState, "generated", StringComparison.Ordinal))
            return Unsupported(EnvironmentUnsupportedReason.GeneratedUnit);
        if (properties.UnsetEnvironment.Count != 0)
            return Unsupported(EnvironmentUnsupportedReason.UnsetEnvironment);
        if (properties.PassEnvironment.Count != 0)
            return Unsupported(EnvironmentUnsupportedReason.PassEnvironment);
        if (properties.EnvironmentFiles.Count + 1 > EnvironmentReadLimits.MaxSources ||
            properties.Environment.Count > EnvironmentReadLimits.MaxAssignments ||
            ConfigurationPaths(properties).Count > EnvironmentReadLimits.MaxConfigurationPaths)
            return Failure(EnvironmentReadFailureCode.LimitExceeded);
        if (properties.FragmentPath.Length == 0)
            return Unsupported(EnvironmentUnsupportedReason.UnsupportedProperty);

        foreach (var path in ConfigurationPaths(properties))
        {
            if (SystemdSourcePath.ExceedsLimit(path))
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            var pathFailure = SystemdSourcePath.Validate(path);
            if (pathFailure is { } reason)
                return Unsupported(reason);
            if (SystemdSourcePath.IsGenerated(path))
                return Unsupported(EnvironmentUnsupportedReason.GeneratedUnit);
        }

        foreach (var file in properties.EnvironmentFiles)
        {
            if (SystemdSourcePath.ExceedsLimit(file.Path))
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            var pathFailure = SystemdSourcePath.Validate(file.Path);
            if (pathFailure is { } reason)
                return Unsupported(reason);
        }
        return null;
    }

    internal static IReadOnlyList<string> ConfigurationPaths(SystemdEnvironmentProperties properties)
    {
        if (properties.FragmentPath.Length == 0)
            return properties.DropInPaths;
        var paths = new string[properties.DropInPaths.Count + 1];
        paths[0] = properties.FragmentPath;
        for (var index = 0; index < properties.DropInPaths.Count; index++)
            paths[index + 1] = properties.DropInPaths[index];
        return paths;
    }

    internal static bool PropertiesEqual(
        SystemdEnvironmentProperties left, SystemdEnvironmentProperties right) =>
        string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
        left.Names.SequenceEqual(right.Names, StringComparer.Ordinal) &&
        string.Equals(left.LoadState, right.LoadState, StringComparison.Ordinal) &&
        string.Equals(left.FragmentPath, right.FragmentPath, StringComparison.Ordinal) &&
        left.DropInPaths.SequenceEqual(right.DropInPaths, StringComparer.Ordinal) &&
        left.NeedDaemonReload == right.NeedDaemonReload &&
        left.Transient == right.Transient &&
        string.Equals(left.UnitFileState, right.UnitFileState, StringComparison.Ordinal) &&
        left.Environment.SequenceEqual(right.Environment, StringComparer.Ordinal) &&
        left.EnvironmentFiles.SequenceEqual(right.EnvironmentFiles) &&
        left.UnsetEnvironment.SequenceEqual(right.UnsetEnvironment, StringComparer.Ordinal) &&
        left.PassEnvironment.SequenceEqual(right.PassEnvironment, StringComparer.Ordinal);

    internal static bool IsDisappearance(SystemdDbusException exception) =>
        exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
        exception.RemoteErrorName is "org.freedesktop.systemd1.NoSuchUnit" or
            "org.freedesktop.DBus.Error.UnknownObject";

    internal static bool IsMissingRequiredProperty(SystemdDbusException exception) =>
        exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
        exception.RemoteErrorName is "org.freedesktop.DBus.Error.UnknownProperty" or
            "org.freedesktop.DBus.Error.UnknownInterface";

    internal static SystemdEnvironmentSourceReadResult.Failure MapFileFailure(
        SystemdSourceFileFailure failure, int? sourceId) => failure switch
        {
            SystemdSourceFileFailure.Missing or SystemdSourceFileFailure.Unavailable =>
                Failure(EnvironmentReadFailureCode.SourceUnavailable, sourceId: sourceId),
            SystemdSourceFileFailure.UnsafePath =>
                Unsupported(EnvironmentUnsupportedReason.UnsafePath, sourceId),
            SystemdSourceFileFailure.Inconsistent =>
                Failure(EnvironmentReadFailureCode.InconsistentSnapshot, sourceId: sourceId),
            SystemdSourceFileFailure.LimitExceeded =>
                Failure(EnvironmentReadFailureCode.LimitExceeded, sourceId: sourceId),
            _ => Failure(EnvironmentReadFailureCode.SourceUnavailable, sourceId: sourceId),
        };

    internal static SystemdEnvironmentSourceReadResult.Failure Unsupported(
        EnvironmentUnsupportedReason reason, int? sourceId = null) =>
        Failure(EnvironmentReadFailureCode.UnsupportedConfiguration, reason, sourceId);

    internal static SystemdEnvironmentSourceReadResult.Failure Failure(
        EnvironmentReadFailureCode code,
        EnvironmentUnsupportedReason? reason = null,
        int? sourceId = null) => new(code, reason, sourceId);

    internal static bool IsExpectedCleanupFailure(Exception exception) =>
        exception is SystemdDbusException or SystemdSourceFileException or
            IOException or UnauthorizedAccessException;
}

internal sealed class SystemdEnvironmentAcquisition
{
    private SystemdEnvironmentAcquisition(
        SystemdEnvironmentAcquisitionSession? session,
        SystemdEnvironmentSourceReadResult.Failure? failure)
    {
        Session = session;
        Failure = failure;
    }

    internal SystemdEnvironmentAcquisitionSession? Session { get; }
    internal SystemdEnvironmentSourceReadResult.Failure? Failure { get; }

    internal static SystemdEnvironmentAcquisition FromSession(
        SystemdEnvironmentAcquisitionSession session) => new(session, null);

    internal static SystemdEnvironmentAcquisition FromFailure(
        SystemdEnvironmentSourceReadResult.Failure failure) => new(null, failure);
}

internal sealed class SystemdEnvironmentAcquisitionSession : IAsyncDisposable
{
    private ISystemdDbusTransport? _transport;
    private readonly ISystemdSourceFileAccess _files;
    private readonly ResolvedSystemdService _resolved;
    private readonly SystemServiceId _requested;
    private readonly SystemdEnvironmentProperties _initial;
    private LoadedEnvironmentResult.Success? _manager;
    private readonly List<SystemdFileObservation> _configuration;
    private readonly List<SystemdFileObservation> _fileObservations;
    private readonly Action<SystemdEnvironmentReadStage>? _stageObserver;
    private bool _snapshotTaken;
    private bool _closed;

    internal SystemdEnvironmentAcquisitionSession(
        ISystemdDbusTransport transport,
        ISystemdSourceFileAccess files,
        ResolvedSystemdService resolved,
        SystemServiceId requested,
        SystemdEnvironmentProperties initial,
        LoadedEnvironmentResult.Success manager,
        List<SystemdFileObservation> configuration,
        List<SystemdFileObservation> fileObservations,
        Action<SystemdEnvironmentReadStage>? stageObserver)
    {
        _transport = transport;
        _files = files;
        _resolved = resolved;
        _requested = requested;
        _initial = initial;
        _manager = manager;
        _configuration = configuration;
        _fileObservations = fileObservations;
        _stageObserver = stageObserver;
    }

    internal SystemdEnvironmentSourceReadResult.Success TakeSnapshot()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_snapshotTaken)
            throw new InvalidOperationException("The acquired source snapshot was already transferred.");

        var manager = _manager ?? throw new InvalidOperationException("The acquisition has no manager source.");
        var published = new List<SystemdEnvironmentFileSource>(_fileObservations.Count);
        try
        {
            for (var index = 0; index < _fileObservations.Count; index++)
            {
                var observation = _fileObservations[index];
                published.Add(new SystemdEnvironmentFileSource(
                    index + 1, _initial.EnvironmentFiles[index].IgnoreErrors,
                    observation.IsMissing,
                    observation.IsMissing ? null : observation.TakeContent()));
            }
            var snapshot = new SystemdEnvironmentSourceReadResult.Success(
                _resolved.Identity.Id, manager, published);
            _manager = null;
            published.Clear();
            _snapshotTaken = true;
            return snapshot;
        }
        finally
        {
            foreach (var source in published)
                source.Dispose();
        }
    }

    internal async Task<SystemdEnvironmentSourceReadResult.Failure?> ValidateAsync(
        SystemdEnvironmentOperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(_closed, this);
        ObserveStage(SystemdEnvironmentReadStage.BeforeFinalValidation, operation);
        try
        {
            SystemdEnvironmentProperties final;
            try
            {
                final = await _transport!.ReadEnvironmentPropertiesAsync(
                        _resolved.Unit, operation.Token)
                    .ConfigureAwait(false);
                operation.ThrowIfStopped();
            }
            catch (SystemdDbusException exception) when (
                SystemdEnvironmentSourceReader.IsDisappearance(exception) ||
                SystemdEnvironmentSourceReader.IsMissingRequiredProperty(exception) ||
                exception.FailureKind is SystemdDbusFailureKind.MalformedReply or
                    SystemdDbusFailureKind.IncompatibleReply)
            {
                return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                    EnvironmentReadFailureCode.InconsistentSnapshot));
            }

            if (!SystemdEnvironmentSourceReader.PropertiesEqual(_initial, final))
                return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                    EnvironmentReadFailureCode.InconsistentSnapshot));
            foreach (var observation in _configuration.Concat(_fileObservations))
            {
                if (!await _files.IsStableAsync(observation, operation.Token).ConfigureAwait(false))
                    return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                        EnvironmentReadFailureCode.InconsistentSnapshot));
                operation.ThrowIfStopped();
            }

            var identityFailure = SystemdEnvironmentSourceReader.ValidateInitial(
                final, _resolved.Identity, _requested);
            return identityFailure is null ? null : operation.Prefer(identityFailure);
        }
        catch (OperationCanceledException) when (operation.CallerCancellation.IsCancellationRequested)
        {
            throw new OperationCanceledException(operation.CallerCancellation);
        }
        catch (OperationCanceledException) when (operation.HasExpired)
        {
            return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                EnvironmentReadFailureCode.Timeout));
        }
        catch (SystemdSourceFileException exception)
        {
            return operation.Prefer(SystemdEnvironmentSourceReader.MapFileFailure(
                exception.Failure, sourceId: null));
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.Timeout && operation.HasExpired)
        {
            return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                EnvironmentReadFailureCode.Timeout));
        }
        catch (SystemdDbusException)
        {
            return operation.Prefer(SystemdEnvironmentSourceReader.Failure(
                EnvironmentReadFailureCode.TransportError));
        }
    }

    internal async ValueTask<SystemdEnvironmentSourceReadResult.Failure?> CloseAsync(
        SystemdEnvironmentOperationContext operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_closed)
            return null;
        _stageObserver?.Invoke(SystemdEnvironmentReadStage.BeforeCleanup);

        Exception? cleanupFailure = null;
        for (var index = _fileObservations.Count - 1; index >= 0; index--)
        {
            try { _fileObservations[index].Dispose(); }
            catch (Exception exception) when (SystemdEnvironmentSourceReader.IsExpectedCleanupFailure(exception))
            { cleanupFailure ??= exception; }
        }
        for (var index = _configuration.Count - 1; index >= 0; index--)
        {
            try { _configuration[index].Dispose(); }
            catch (Exception exception) when (SystemdEnvironmentSourceReader.IsExpectedCleanupFailure(exception))
            { cleanupFailure ??= exception; }
        }
        _manager?.Dispose();
        _manager = null;

        var transport = _transport;
        _transport = null;
        if (transport is not null)
        {
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (SystemdEnvironmentSourceReader.IsExpectedCleanupFailure(exception))
            { cleanupFailure ??= exception; }
        }

        _closed = true;
        operation.CallerCancellation.ThrowIfCancellationRequested();
        if (operation.HasExpired)
            return SystemdEnvironmentSourceReader.Failure(EnvironmentReadFailureCode.Timeout);
        return cleanupFailure is null
            ? null
            : SystemdEnvironmentSourceReader.Failure(EnvironmentReadFailureCode.TransportError);
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        for (var index = _fileObservations.Count - 1; index >= 0; index--)
            _fileObservations[index].Dispose();
        for (var index = _configuration.Count - 1; index >= 0; index--)
            _configuration[index].Dispose();
        _manager?.Dispose();
        _manager = null;
        var transport = _transport;
        _transport = null;
        _closed = true;
        if (transport is not null)
        {
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) when (SystemdEnvironmentSourceReader.IsExpectedCleanupFailure(exception))
            {
                // Best-effort fallback only; successful publication always uses CloseAsync first.
            }
        }
    }

    private void ObserveStage(
        SystemdEnvironmentReadStage stage, SystemdEnvironmentOperationContext operation)
    {
        operation.ThrowIfStopped();
        _stageObserver?.Invoke(stage);
        operation.ThrowIfStopped();
    }
}
