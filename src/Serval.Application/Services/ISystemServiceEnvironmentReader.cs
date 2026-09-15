using Serval.Domain.Services;

namespace Serval.Application.Services;

/// <summary>Reads supported environment declarations, never the complete process environment.</summary>
public interface ISystemServiceEnvironmentReader
{
    /// <summary>
    /// Returns all supported declarations or a value-free failure. Caller cancellation
    /// propagates as OperationCanceledException; the internal deadline returns Timeout.
    /// The future Agent must authorize before invoking this library operation.
    /// </summary>
    Task<ServiceEnvironmentReadResult> ReadAsync(
        SystemServiceId serviceId,
        CancellationToken cancellationToken);
}
