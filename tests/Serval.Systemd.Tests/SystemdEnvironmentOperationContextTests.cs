using Serval.Application.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdEnvironmentOperationContextTests
{
    [Fact]
    public void ReportsDecreasingAllowanceUntilExactExpiry()
    {
        var time = new ControlledTimeProvider();
        using var operation = new SystemdEnvironmentOperationContext(time, CancellationToken.None);

        time.Advance(EnvironmentReadLimits.OperationDeadline - TimeSpan.FromTicks(1));
        Assert.Equal(TimeSpan.FromTicks(1), operation.RemainingAllowance);
        operation.ThrowIfStopped();

        time.Advance(TimeSpan.FromTicks(1));
        Assert.True(operation.HasExpired);
        Assert.Throws<SystemdEnvironmentDeadlineException>(operation.ThrowIfStopped);
    }

    [Fact]
    public void MonotonicElapsedCheckDoesNotDependOnTimerDelivery()
    {
        var time = new ControlledTimeProvider { DeliverTimers = false };
        using var operation = new SystemdEnvironmentOperationContext(time, CancellationToken.None);

        time.Advance(EnvironmentReadLimits.OperationDeadline);

        Assert.False(operation.Token.IsCancellationRequested);
        Assert.True(operation.HasExpired);
        var failure = operation.Prefer(new ServiceEnvironmentReadResult.Failure(
            EnvironmentReadFailureCode.TransportError));
        Assert.Equal(EnvironmentReadFailureCode.Timeout, failure.Code);
    }

    [Fact]
    public void OriginalCallerCancellationPrecedesDeadlineAndKeepsItsToken()
    {
        var time = new ControlledTimeProvider();
        using var caller = new CancellationTokenSource();
        using var operation = new SystemdEnvironmentOperationContext(time, caller.Token);
        time.Advance(EnvironmentReadLimits.OperationDeadline);
        caller.Cancel();

        var exception = Assert.ThrowsAny<OperationCanceledException>(() => operation.Prefer(
            new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.TransportError)));

        Assert.Equal(caller.Token, exception.CancellationToken);
    }

    internal sealed class ControlledTimeProvider : TimeProvider
    {
        private readonly List<ControlledTimer> _timers = [];
        private long _timestamp;

        internal bool DeliverTimers { get; init; } = true;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            if (!DeliverTimers)
                return;
            foreach (var timer in _timers.ToArray())
                timer.FireIfDue(_timestamp);
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ControlledTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ControlledTimer : ITimer
        {
            private readonly ControlledTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private long _dueAt;
            private TimeSpan _period;
            private bool _disposed;

            internal ControlledTimer(
                ControlledTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _dueAt = owner._timestamp + dueTime.Ticks;
                _period = period;
            }

            internal void FireIfDue(long now)
            {
                if (_disposed || now < _dueAt)
                    return;
                _callback(_state);
                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : now + _period.Ticks;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                    return false;
                _dueAt = _owner._timestamp + dueTime.Ticks;
                _period = period;
                return true;
            }

            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
