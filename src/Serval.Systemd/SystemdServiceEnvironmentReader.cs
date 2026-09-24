using Serval.Application.Services;
using Serval.Domain.Services;

namespace Serval.Systemd;

/// <summary>
/// Reads supported systemd environment declarations through the application contract.
/// Authorization remains the responsibility of the future privileged Agent caller.
/// </summary>
public sealed class SystemdServiceEnvironmentReader : ISystemServiceEnvironmentReader
{
    private readonly SystemdEnvironmentSourceReader _sourceReader;
    private readonly TimeProvider _timeProvider;
    private readonly Action<SystemdEnvironmentReadStage>? _stageObserver;
    private readonly Action<Array>? _clearedParserBufferObserver;
    private readonly Action<char[]>? _clearedOutputBufferObserver;
    private readonly SystemdEnvironmentComposer.ResultFactory? _resultFactory;
    private readonly Action<EnvironmentFileParser.Phase, int>? _parserProgressObserver;
    private readonly Action<SystemdEnvironmentComposer.Phase, int>? _composerProgressObserver;

    public SystemdServiceEnvironmentReader()
        : this(
            static async (allowance, token) => await DBus.SystemdDbusTransport.ConnectAsync(
                allowance, token).ConfigureAwait(false),
            new LinuxSystemdSourceFileAccess(),
            TimeProvider.System)
    {
    }

    internal SystemdServiceEnvironmentReader(
        SystemdEnvironmentTransportFactory connect,
        ISystemdSourceFileAccess files,
        TimeProvider timeProvider,
        Action<SystemdEnvironmentReadStage>? stageObserver = null,
        Action<Array>? clearedParserBufferObserver = null,
        Action<char[]>? clearedOutputBufferObserver = null,
        SystemdEnvironmentComposer.ResultFactory? resultFactory = null,
        Action<EnvironmentFileParser.Phase, int>? parserProgressObserver = null,
        Action<SystemdEnvironmentComposer.Phase, int>? composerProgressObserver = null)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(files);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _stageObserver = stageObserver;
        _clearedParserBufferObserver = clearedParserBufferObserver;
        _clearedOutputBufferObserver = clearedOutputBufferObserver;
        _resultFactory = resultFactory;
        _parserProgressObserver = parserProgressObserver;
        _composerProgressObserver = composerProgressObserver;
        _sourceReader = new SystemdEnvironmentSourceReader(
            connect, files, timeProvider, stageObserver);
    }

    public async Task<ServiceEnvironmentReadResult> ReadAsync(
        SystemServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(serviceId);
        using var operation = new SystemdEnvironmentOperationContext(
            _timeProvider, cancellationToken);
        ServiceEnvironmentReadResult.Success? provisional = null;
        var transferred = false;
        try
        {
            var acquisition = await _sourceReader.AcquireAsync(serviceId, operation)
                .ConfigureAwait(false);
            if (acquisition.Failure is { } acquisitionFailure)
                return operation.Prefer(Copy(acquisitionFailure));

            await using var session = acquisition.Session!;
            var snapshot = session.TakeSnapshot();
            var candidates = new EnvironmentFileParseResult.Success?[snapshot.FileSources.Count];
            var compositionOwnsInputs = false;
            try
            {
                var remainingAssignments = EnvironmentReadLimits.MaxAssignments -
                    snapshot.ManagerSource.Assignments;
                for (var index = 0; index < snapshot.FileSources.Count; index++)
                {
                    var source = snapshot.FileSources[index];
                    if (source.IsMissing)
                        continue;

                    ObserveStage(SystemdEnvironmentReadStage.BeforeParsing, operation);
                    var parsed = EnvironmentFileParser.Parse(
                        source.Reveal(), source.SourceId, remainingAssignments,
                        _parserProgressObserver, _clearedParserBufferObserver, operation.Token);
                    if (parsed is EnvironmentFileParseResult.Failure parseFailure)
                    {
                        var failure = new ServiceEnvironmentReadResult.Failure(
                            parseFailure.Code, sourceId: parseFailure.SourceId);
                        await CloseBeforeFailureAsync(session, operation).ConfigureAwait(false);
                        return operation.Prefer(failure);
                    }

                    var candidate = (EnvironmentFileParseResult.Success)parsed;
                    candidates[index] = candidate;
                    remainingAssignments -= candidate.Assignments;
                }

                ObserveStage(SystemdEnvironmentReadStage.BeforeComposition, operation);
                compositionOwnsInputs = true;
                var composed = SystemdEnvironmentComposer.Compose(
                    snapshot, candidates, _composerProgressObserver,
                    _clearedOutputBufferObserver, _resultFactory, operation.Token);
                if (composed is ServiceEnvironmentReadResult.Failure compositionFailure)
                {
                    await CloseBeforeFailureAsync(session, operation).ConfigureAwait(false);
                    return operation.Prefer(compositionFailure);
                }

                provisional = (ServiceEnvironmentReadResult.Success)composed;
                var validationFailure = await session.ValidateAsync(operation).ConfigureAwait(false);
                if (validationFailure is not null)
                {
                    await CloseBeforeFailureAsync(session, operation).ConfigureAwait(false);
                    return operation.Prefer(Copy(validationFailure));
                }

                var cleanupFailure = await session.CloseAsync(operation).ConfigureAwait(false);
                if (cleanupFailure is not null)
                    return operation.Prefer(Copy(cleanupFailure));

                ObserveStage(SystemdEnvironmentReadStage.BeforePublication, operation);
                operation.ThrowIfStopped();
                transferred = true;
                return provisional;
            }
            finally
            {
                if (!compositionOwnsInputs)
                {
                    for (var index = candidates.Length - 1; index >= 0; index--)
                        candidates[index]?.Dispose();
                    snapshot.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (operation.HasExpired)
        {
            return operation.Prefer(new ServiceEnvironmentReadResult.Failure(
                EnvironmentReadFailureCode.Timeout));
        }
        finally
        {
            if (!transferred)
                provisional?.Dispose();
        }
    }

    private static async Task CloseBeforeFailureAsync(
        SystemdEnvironmentAcquisitionSession session,
        SystemdEnvironmentOperationContext operation)
    {
        // A primary controlled failure remains primary; close still runs before it is returned.
        _ = await session.CloseAsync(operation).ConfigureAwait(false);
    }

    private void ObserveStage(
        SystemdEnvironmentReadStage stage,
        SystemdEnvironmentOperationContext operation)
    {
        operation.ThrowIfStopped();
        _stageObserver?.Invoke(stage);
        operation.ThrowIfStopped();
    }

    private static ServiceEnvironmentReadResult.Failure Copy(
        SystemdEnvironmentSourceReadResult.Failure failure) =>
        new(failure.Code, failure.Reason, failure.SourceId);

    private static void ValidateRequest(SystemServiceId serviceId)
    {
        ArgumentNullException.ThrowIfNull(serviceId);
        _ = new SystemServiceId(serviceId.Value);
        if (SystemdServiceIdentity.IsTemplate(serviceId.Value))
            throw new ArgumentException(
                "Environment reading requires a concrete service identifier.", nameof(serviceId));
    }
}
