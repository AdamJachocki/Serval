using Serval.Application.Services;

namespace Serval.Systemd;

/// <summary>Owns the single managed lifetime shared by every environment-read stage.</summary>
internal sealed class SystemdEnvironmentOperationContext : IDisposable
{
    private readonly CancellationTokenSource _deadlineCancellation;
    private readonly CancellationTokenSource _linkedCancellation;
    private readonly TimeProvider _timeProvider;
    private readonly long _startedAt;
    private bool _disposed;

    internal SystemdEnvironmentOperationContext(
        TimeProvider timeProvider,
        CancellationToken callerCancellation)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        CallerCancellation = callerCancellation;
        _startedAt = timeProvider.GetTimestamp();
        _deadlineCancellation = new CancellationTokenSource(
            EnvironmentReadLimits.OperationDeadline,
            timeProvider);
        _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation,
            _deadlineCancellation.Token);
    }

    internal CancellationToken CallerCancellation { get; }
    internal CancellationToken Token => _linkedCancellation.Token;

    internal TimeSpan RemainingAllowance
    {
        get
        {
            ThrowIfStopped();
            var remaining = EnvironmentReadLimits.OperationDeadline - Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    internal bool HasExpired =>
        _deadlineCancellation.IsCancellationRequested ||
        Elapsed >= EnvironmentReadLimits.OperationDeadline;

    internal void ThrowIfStopped()
    {
        CallerCancellation.ThrowIfCancellationRequested();
        if (HasExpired)
            throw new SystemdEnvironmentDeadlineException();
        Token.ThrowIfCancellationRequested();
    }

    internal ServiceEnvironmentReadResult.Failure Prefer(
        ServiceEnvironmentReadResult.Failure failure)
    {
        CallerCancellation.ThrowIfCancellationRequested();
        return HasExpired
            ? new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.Timeout)
            : failure;
    }

    internal SystemdEnvironmentSourceReadResult.Failure Prefer(
        SystemdEnvironmentSourceReadResult.Failure failure)
    {
        CallerCancellation.ThrowIfCancellationRequested();
        return HasExpired
            ? new SystemdEnvironmentSourceReadResult.Failure(EnvironmentReadFailureCode.Timeout)
            : failure;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _linkedCancellation.Dispose();
        _deadlineCancellation.Dispose();
        _disposed = true;
    }

    private TimeSpan Elapsed => _timeProvider.GetElapsedTime(_startedAt, _timeProvider.GetTimestamp());
}

internal sealed class SystemdEnvironmentDeadlineException : OperationCanceledException
{
    internal SystemdEnvironmentDeadlineException()
        : base("The managed environment read deadline expired.")
    {
    }
}
