using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Serval.EnvironmentOracle;

internal enum ExpectationManifestError
{
    Malformed,
    DuplicateName,
    TrailingData,
    OverLimit,
}

internal sealed class ExpectationManifestException : Exception
{
    internal ExpectationManifestException(ExpectationManifestError error)
        : base("The expectation manifest is invalid.")
    {
        Error = error;
    }

    internal ExpectationManifestError Error { get; }
}

internal readonly record struct ExpectationSpec(string Name, string? Value);

internal sealed class ExpectationManifest : IDisposable
{
    internal const int MaximumBytes = 1024 * 1024;
    internal const int MaximumEntries = 16_384;
    internal const int MaximumNameBytes = 255;
    internal const string CredentialName = "serval-environment-expectations";

    private static readonly byte[] Magic = "SERVALM1"u8.ToArray();
    private static readonly SearchValues<char> NameCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _buffer;
    private readonly ManifestEntry[] _entries;
    private readonly int _length;
    private bool _disposed;
    private bool _wasCleared;

    private ExpectationManifest(byte[] buffer, int length, ManifestEntry[] entries)
    {
        _buffer = buffer;
        _length = length;
        _entries = entries;
    }

    internal IReadOnlyList<ManifestEntry> Entries
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries;
        }
    }

    internal static ExpectationManifest Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var buffer = ArrayPool<byte>.Shared.Rent(MaximumBytes + 1);
        var length = 0;
        try
        {
            while (length <= MaximumBytes)
            {
                var read = source.Read(buffer, length, MaximumBytes + 1 - length);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumBytes || source.ReadByte() != -1)
            {
                throw new ExpectationManifestException(ExpectationManifestError.OverLimit);
            }

            var entries = Parse(buffer.AsSpan(0, length));
            return new ExpectationManifest(buffer, length, entries);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, length));
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    internal static void Write(Stream destination, IReadOnlyList<ExpectationSpec> expectations)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(expectations);

        if (expectations.Count > MaximumEntries)
        {
            throw new ExpectationManifestException(ExpectationManifestError.OverLimit);
        }

        using var buffer = new MemoryStream();
        try
        {
            buffer.Write(Magic);
            WriteUInt32(buffer, checked((uint)expectations.Count));

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var expectation in expectations)
            {
                ValidateName(expectation.Name);
                if (!names.Add(expectation.Name))
                {
                    throw new ExpectationManifestException(ExpectationManifestError.DuplicateName);
                }

                var nameBytes = StrictUtf8.GetBytes(expectation.Name);
                var valueBytes = expectation.Value is null ? [] : StrictUtf8.GetBytes(expectation.Value);
                try
                {
                    buffer.WriteByte(expectation.Value is null ? (byte)0 : (byte)1);
                    WriteUInt32(buffer, checked((uint)nameBytes.Length));
                    WriteUInt32(buffer, checked((uint)valueBytes.Length));
                    buffer.Write(nameBytes);
                    buffer.Write(valueBytes);
                    if (buffer.Length > MaximumBytes)
                    {
                        throw new ExpectationManifestException(ExpectationManifestError.OverLimit);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(valueBytes);
                }
            }

            buffer.Position = 0;
            buffer.CopyTo(destination);
        }
        finally
        {
            if (buffer.TryGetBuffer(out var segment))
            {
                CryptographicOperations.ZeroMemory(segment.AsSpan(0, checked((int)buffer.Length)));
            }
        }
    }

    internal ReadOnlySpan<byte> GetValueBytes(ManifestEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return entry.IsPresent ? _buffer.AsSpan(entry.ValueOffset, entry.ValueLength) : [];
    }

    internal bool OwnedBufferIsClearedForTests()
    {
        return _wasCleared;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_buffer.AsSpan(0, _length));
        _wasCleared = true;
        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    private static ManifestEntry[] Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Magic.Length + sizeof(uint) || !bytes[..Magic.Length].SequenceEqual(Magic))
        {
            throw new ExpectationManifestException(ExpectationManifestError.Malformed);
        }

        var offset = Magic.Length;
        var count = ReadUInt32(bytes, ref offset);
        if (count > MaximumEntries)
        {
            throw new ExpectationManifestException(ExpectationManifestError.OverLimit);
        }

        var entries = new ManifestEntry[count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            if (offset >= bytes.Length)
            {
                throw new ExpectationManifestException(ExpectationManifestError.Malformed);
            }

            var kind = bytes[offset++];
            if (kind > 1)
            {
                throw new ExpectationManifestException(ExpectationManifestError.Malformed);
            }

            var nameLength = ReadLength(bytes, ref offset, MaximumNameBytes);
            var valueLength = ReadLength(bytes, ref offset, MaximumBytes);
            if (kind == 0 && valueLength != 0)
            {
                throw new ExpectationManifestException(ExpectationManifestError.Malformed);
            }

            var required = checked(nameLength + valueLength);
            if (required > bytes.Length - offset)
            {
                throw new ExpectationManifestException(ExpectationManifestError.Malformed);
            }

            string name;
            try
            {
                name = StrictUtf8.GetString(bytes.Slice(offset, nameLength));
            }
            catch (DecoderFallbackException)
            {
                throw new ExpectationManifestException(ExpectationManifestError.Malformed);
            }

            ValidateName(name);
            if (!names.Add(name))
            {
                throw new ExpectationManifestException(ExpectationManifestError.DuplicateName);
            }

            offset += nameLength;
            if (kind == 1)
            {
                try
                {
                    _ = StrictUtf8.GetCharCount(bytes.Slice(offset, valueLength));
                }
                catch (DecoderFallbackException)
                {
                    throw new ExpectationManifestException(ExpectationManifestError.Malformed);
                }
            }

            entries[index] = new ManifestEntry(name, kind == 1, offset, valueLength);
            offset += valueLength;
        }

        if (offset != bytes.Length)
        {
            throw new ExpectationManifestException(ExpectationManifestError.TrailingData);
        }

        return entries;
    }

    private static int ReadLength(ReadOnlySpan<byte> bytes, ref int offset, int maximum)
    {
        var value = ReadUInt32(bytes, ref offset);
        if (value > maximum)
        {
            throw new ExpectationManifestException(ExpectationManifestError.OverLimit);
        }

        return checked((int)value);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, ref int offset)
    {
        if (sizeof(uint) > bytes.Length - offset)
        {
            throw new ExpectationManifestException(ExpectationManifestError.Malformed);
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
        offset += sizeof(uint);
        return value;
    }

    private static void WriteUInt32(Stream destination, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        destination.Write(bytes);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || StrictUtf8.GetByteCount(name) > MaximumNameBytes ||
            !IsNameStart(name[0]) || name.AsSpan(1).ContainsAnyExcept(NameCharacters))
        {
            throw new ExpectationManifestException(ExpectationManifestError.Malformed);
        }
    }

    private static bool IsNameStart(char value)
    {
        return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
    }

    internal readonly record struct ManifestEntry(string Name, bool IsPresent, int ValueOffset, int ValueLength);
}
