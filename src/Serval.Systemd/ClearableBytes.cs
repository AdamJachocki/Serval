using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Serval.Systemd;

[DebuggerDisplay("[REDACTED]")]
internal sealed class ClearableBytes : IDisposable
{
    private byte[] _buffer;
    private readonly Action<byte[]>? _clearedBufferObserver;
    private bool _disposed;

    internal ClearableBytes(int capacity = 0, Action<byte[]>? clearedBufferObserver = null)
    {
        if (capacity < 0 || capacity > EnvironmentReadLimits.MaxSourceBytes)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = capacity == 0 ? [] : new byte[capacity];
        _clearedBufferObserver = clearedBufferObserver;
    }

    internal int Length { get; private set; }

    internal ReadOnlySpan<byte> Reveal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _buffer.AsSpan(0, Length);
    }

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (bytes.Length > EnvironmentReadLimits.MaxSourceBytes - Length)
            throw new SystemdSourceFileException(SystemdSourceFileFailure.LimitExceeded);

        EnsureCapacity(Length + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(Length));
        Length += bytes.Length;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear(_buffer);
        _clearedBufferObserver?.Invoke(_buffer);
        _buffer = [];
        Length = 0;
        _disposed = true;
    }

    public override string ToString() => "[REDACTED]";

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
            return;
        var nextLength = Math.Min(
            EnvironmentReadLimits.MaxSourceBytes,
            Math.Max(required, Math.Max(4096, _buffer.Length * 2)));
        var replacement = new byte[nextLength];
        _buffer.AsSpan(0, Length).CopyTo(replacement);
        Clear(_buffer);
        _clearedBufferObserver?.Invoke(_buffer);
        _buffer = replacement;
    }

    private static void Clear(byte[] buffer) =>
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));
}
