namespace Serval.Agent;

/// <summary>
/// Caps synchronous native calls that cannot honor managed cancellation. A timed-out
/// call keeps its slot until the native function returns.
/// </summary>
public sealed class BoundedNativeCalls(int maximumConcurrentCalls, TimeSpan waitLimit)
{
    private readonly int maximum = ValidateMaximum(maximumConcurrentCalls);
    private int active;

    public async Task<NativeCallResult<T>> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (waitLimit <= TimeSpan.Zero || waitLimit > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException("Native call wait limit is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryAcquire())
        {
            return new NativeCallResult<T>(false, default);
        }

        Task<T> pending;
        try
        {
            pending = Task.Factory.StartNew(() =>
            {
                try
                {
                    return operation();
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch
        {
            Interlocked.Decrement(ref active);
            throw;
        }

        try
        {
            var value = await pending.WaitAsync(waitLimit, cancellationToken).ConfigureAwait(false);
            return new NativeCallResult<T>(true, value);
        }
        catch (TimeoutException)
        {
            ObserveLateFailure(pending);
            return new NativeCallResult<T>(false, default);
        }
        catch (OperationCanceledException)
        {
            ObserveLateFailure(pending);
            throw;
        }
    }

    private static int ValidateMaximum(int count) => count is >= 1 and <= 32
        ? count
        : throw new ArgumentOutOfRangeException(nameof(count));

    private bool TryAcquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref active);
            if (current >= maximum)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref active, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private static void ObserveLateFailure<T>(Task<T> pending)
    {
        _ = pending.ContinueWith(static task => _ = task.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}

public readonly record struct NativeCallResult<T>(bool Succeeded, T? Value);
