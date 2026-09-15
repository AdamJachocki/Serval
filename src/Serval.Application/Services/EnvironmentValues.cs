using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Serval.Application.Services;

/// <summary>
/// Owns sensitive buffers until disposed. Internal readers borrow spans only until the next
/// mutation or disposal. This type is request-local and must not be shared between threads.
/// </summary>
[DebuggerDisplay("[REDACTED]")]
[JsonConverter(typeof(EnvironmentValuesJsonConverter))]
public sealed class EnvironmentValues : IDisposable
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly Dictionary<string, char[]> values = new(StringComparer.Ordinal);
    private bool disposed;
    private bool frozen;

    internal EnvironmentValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        try
        {
            foreach (var pair in values)
            {
                if (pair.Value is null || this.values.ContainsKey(pair.Key))
                    throw new ArgumentException("Invalid environment values.");
                Set(pair.Key, pair.Value.AsSpan());
            }
        }
        catch (OperationCanceledException error)
        {
            Dispose();
            throw new OperationCanceledException("Environment value creation canceled.", error.CancellationToken);
        }
        catch (Exception)
        {
            Dispose();
            throw new ArgumentException("Invalid environment values.", nameof(values));
        }
    }

    /// <summary>Copies the input; the caller retains ownership of its original storage.</summary>
    internal void Set(string name, ReadOnlySpan<char> value)
    {
        ThrowIfDisposed();
        if (frozen)
            throw new InvalidOperationException("Environment values are frozen.");
        _ = new EnvironmentVariableMetadata(name, 0);
        var buffer = value.ToArray();
        try
        {
            values.TryGetValue(name, out var previous);
            values[name] = buffer;
            if (previous is not null)
                Clear(previous);
        }
        catch (Exception)
        {
            Clear(buffer);
            throw new InvalidOperationException("Environment value assignment failed.");
        }
    }

    /// <summary>Explicit internal access. Never use in diagnostics or retain the borrowed span.</summary>
    internal ReadOnlySpan<char> Reveal(string name)
    {
        ThrowIfDisposed();
        return values.TryGetValue(name, out var value)
            ? value : throw new KeyNotFoundException("Environment variable is absent.");
    }

    internal bool HasExactly(IEnumerable<string> names)
    {
        ThrowIfDisposed();
        return values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names);
    }

    internal void Freeze()
    {
        ThrowIfDisposed();
        frozen = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        foreach (var buffer in values.Values)
            Clear(buffer);
        values.Clear();
        disposed = true;
    }

    private static void Clear(char[] buffer) => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(buffer.AsSpan()));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, typeof(EnvironmentValues));

    public override string ToString() => "[REDACTED]";
}

/// <summary>System.Text.Json emits a constant mask, including for empty or disposed values.</summary>
internal sealed class EnvironmentValuesJsonConverter : JsonConverter<EnvironmentValues>
{
    public override EnvironmentValues Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new JsonException("Environment values cannot be deserialized.");

    public override void Write(Utf8JsonWriter writer, EnvironmentValues value, JsonSerializerOptions options)
        => writer.WriteStringValue("[REDACTED]");
}
