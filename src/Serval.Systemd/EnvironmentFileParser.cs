using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Serval.Application.Services;

namespace Serval.Systemd;

/// <summary>Parses caller-owned raw bytes without file I/O, expansion, or execution.</summary>
internal static class EnvironmentFileParser
{
    internal enum Phase { TextValidation, StructureValidation, Decoding, BeforePublication }

    private enum State
    {
        PreKey,
        Key,
        PreValue,
        Value,
        ValueEscape,
        SingleQuotedValue,
        DoubleQuotedValue,
        DoubleQuotedValueEscape,
        Comment,
        CommentEscape,
    }

    internal static EnvironmentFileParseResult Parse(ReadOnlySpan<byte> source, int sourceId,
        int remainingAssignments, CancellationToken cancellationToken)
        => Parse(source, sourceId, remainingAssignments, null, null, cancellationToken);

    internal static EnvironmentFileParseResult Parse(ReadOnlySpan<byte> source, int sourceId,
        int remainingAssignments, Action<Phase, int>? progressObserver, Action<Array>? clearedBufferObserver,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceId, 1);
        if ((uint)remainingAssignments > EnvironmentReadLimits.MaxAssignments)
            throw new ArgumentOutOfRangeException(nameof(remainingAssignments));

        if (source.Length > EnvironmentReadLimits.MaxSourceBytes)
            return Failure(EnvironmentReadFailureCode.LimitExceeded, sourceId);
        if (!HasValidText(source, progressObserver, cancellationToken))
            return Failure(EnvironmentReadFailureCode.InvalidSource, sourceId);

        var validation = RunPass(source, remainingAssignments, null, null,
            Phase.StructureValidation, progressObserver, cancellationToken);
        if (validation.Code is not null)
            return Failure(validation.Code.Value, sourceId);

        var values = new EnvironmentValues([], buffer => clearedBufferObserver?.Invoke(buffer));
        try
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var decoding = RunPass(source, remainingAssignments, values, names,
                Phase.Decoding, progressObserver, clearedBufferObserver, cancellationToken);
            if (decoding.Code is not null)
                throw new InvalidOperationException("Validated environment source changed during parsing.");

            var variables = names.Order(StringComparer.Ordinal)
                .Select(name => new EnvironmentVariableMetadata(name, sourceId)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            progressObserver?.Invoke(Phase.BeforePublication, source.Length);
            cancellationToken.ThrowIfCancellationRequested();
            values.Freeze();
            return new EnvironmentFileParseResult.Success(
                sourceId, values, Array.AsReadOnly(variables), source.Length, validation.Assignments);
        }
        catch
        {
            values.Dispose();
            throw;
        }
    }

    private static PassResult RunPass(ReadOnlySpan<byte> source, int remainingAssignments,
        EnvironmentValues? values, HashSet<string>? names, Phase phase,
        Action<Phase, int>? progressObserver, CancellationToken cancellationToken)
        => RunPass(source, remainingAssignments, values, names, phase, progressObserver, null, cancellationToken);

    private static PassResult RunPass(ReadOnlySpan<byte> source, int remainingAssignments,
        EnvironmentValues? values, HashSet<string>? names, Phase phase,
        Action<Phase, int>? progressObserver, Action<Array>? clearedBufferObserver,
        CancellationToken cancellationToken)
    {
        var state = State.PreKey;
        var keyStart = 0;
        var keyEnd = 0;
        var assignments = 0;
        var recordBytes = 0;
        using var value = values is null ? null : new SensitiveByteBuffer(clearedBufferObserver);
        var lastValueWhitespace = -1;

        for (var index = 0; index < source.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressObserver?.Invoke(phase, index);
            cancellationToken.ThrowIfCancellationRequested();
            var current = source[index];
            recordBytes++;
            if (recordBytes > EnvironmentReadLimits.MaxLogicalRecordBytes)
                return new(EnvironmentReadFailureCode.LimitExceeded, assignments);

            switch (state)
            {
                case State.PreKey:
                    if (IsNewline(current))
                    {
                        recordBytes = 0;
                    }
                    else if (IsWhitespace(current))
                    {
                        // Leading whitespace is syntax and remains part of the record byte count.
                    }
                    else if (current is (byte)'#' or (byte)';')
                    {
                        state = State.Comment;
                    }
                    else if (current == (byte)'=')
                    {
                        return new(EnvironmentReadFailureCode.InvalidSource, assignments);
                    }
                    else
                    {
                        state = State.Key;
                        keyStart = index;
                        keyEnd = index + 1;
                    }

                    break;

                case State.Key:
                    if (IsNewline(current))
                    {
                        state = State.PreKey;
                        recordBytes = 0;
                    }
                    else if (current == (byte)'=')
                    {
                        if (!IsName(source[keyStart..keyEnd]))
                            return new(EnvironmentReadFailureCode.InvalidSource, assignments);
                        assignments++;
                        if (assignments > remainingAssignments)
                            return new(EnvironmentReadFailureCode.LimitExceeded, assignments);
                        state = State.PreValue;
                        value?.Reset();
                        lastValueWhitespace = -1;
                    }
                    else if (!IsWhitespace(current))
                    {
                        keyEnd = index + 1;
                    }

                    break;

                case State.PreValue:
                    if (IsNewline(current))
                    {
                        Publish(source[keyStart..keyEnd], value, values, names);
                        state = State.PreKey;
                        recordBytes = 0;
                    }
                    else if (current == (byte)'\'')
                    {
                        state = State.SingleQuotedValue;
                    }
                    else if (current == (byte)'"')
                    {
                        state = State.DoubleQuotedValue;
                    }
                    else if (current == (byte)'\\')
                    {
                        state = State.ValueEscape;
                    }
                    else if (!IsWhitespace(current))
                    {
                        state = State.Value;
                        value?.Append(current);
                    }

                    break;

                case State.Value:
                    if (IsNewline(current))
                    {
                        if (lastValueWhitespace >= 0)
                            value?.Truncate(lastValueWhitespace);
                        Publish(source[keyStart..keyEnd], value, values, names);
                        state = State.PreKey;
                        recordBytes = 0;
                    }
                    else if (current == (byte)'\\')
                    {
                        state = State.ValueEscape;
                        lastValueWhitespace = -1;
                    }
                    else
                    {
                        if (!IsWhitespace(current))
                            lastValueWhitespace = -1;
                        else if (lastValueWhitespace < 0)
                            lastValueWhitespace = value?.Length ?? 0;
                        value?.Append(current);
                    }

                    break;

                case State.ValueEscape:
                    state = State.Value;
                    if (!IsNewline(current))
                        value?.Append(current);
                    break;

                case State.SingleQuotedValue:
                    if (current == (byte)'\'')
                        state = State.PreValue;
                    else
                        value?.Append(current);
                    break;

                case State.DoubleQuotedValue:
                    if (current == (byte)'"')
                        state = State.PreValue;
                    else if (current == (byte)'\\')
                        state = State.DoubleQuotedValueEscape;
                    else
                        value?.Append(current);
                    break;

                case State.DoubleQuotedValueEscape:
                    state = State.DoubleQuotedValue;
                    if (NeedsDoubleQuoteEscape(current))
                        value?.Append(current);
                    else if (current != (byte)'\n')
                    {
                        value?.Append((byte)'\\');
                        value?.Append(current);
                    }

                    break;

                case State.Comment:
                    if (current == (byte)'\\')
                    {
                        state = State.CommentEscape;
                    }
                    else if (IsNewline(current))
                    {
                        state = State.PreKey;
                        recordBytes = 0;
                    }

                    break;

                case State.CommentEscape:
                    if (IsNewline(current))
                    {
                        if (!(current == (byte)'\r' && index + 1 < source.Length && source[index + 1] == (byte)'\n'))
                            return new(EnvironmentReadFailureCode.InvalidSource, assignments);
                        state = State.PreKey;
                        recordBytes = 0;
                    }
                    else
                    {
                        state = State.Comment;
                    }

                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (state is State.SingleQuotedValue or State.DoubleQuotedValue or State.DoubleQuotedValueEscape)
            return new(EnvironmentReadFailureCode.InvalidSource, assignments);
        if (state is State.PreValue or State.Value or State.ValueEscape)
        {
            if (state == State.Value && lastValueWhitespace >= 0)
                value?.Truncate(lastValueWhitespace);
            Publish(source[keyStart..keyEnd], value, values, names);
        }

        return new(null, assignments);
    }

    private static bool HasValidText(ReadOnlySpan<byte> source, Action<Phase, int>? progressObserver,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (!source.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progressObserver?.Invoke(Phase.TextValidation, offset);
            cancellationToken.ThrowIfCancellationRequested();
            var status = Rune.DecodeFromUtf8(source, out var rune, out var consumed);
            if (status != OperationStatus.Done || rune.Value is 0 or 0xFEFF || IsNoncharacter(rune.Value))
                return false;
            source = source[consumed..];
            offset += consumed;
        }

        return true;
    }

    private static bool IsNoncharacter(int scalar) =>
        scalar is >= 0xFDD0 and <= 0xFDEF || (scalar & 0xFFFE) == 0xFFFE;

    private static bool IsName(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || !(IsAsciiLetter(name[0]) || name[0] == (byte)'_'))
            return false;
        foreach (var character in name[1..])
            if (!(IsAsciiLetter(character) || char.IsAsciiDigit((char)character) || character == (byte)'_'))
                return false;
        return true;
    }

    private static bool IsAsciiLetter(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    private static bool IsWhitespace(byte value) => value is (byte)' ' or (byte)'\t';

    private static bool IsNewline(byte value) => value is (byte)'\r' or (byte)'\n';

    private static bool NeedsDoubleQuoteEscape(byte value) =>
        value is (byte)'"' or (byte)'\\' or (byte)'$' or (byte)'`';

    private static void Publish(ReadOnlySpan<byte> nameBytes, SensitiveByteBuffer? value,
        EnvironmentValues? values, HashSet<string>? names)
    {
        if (values is null)
            return;
        var name = Encoding.ASCII.GetString(nameBytes);
        using var characters = value!.DecodeUtf8();
        values.Set(name, characters.Span);
        names!.Add(name);
        value.Reset();
    }

    private static EnvironmentFileParseResult.Failure Failure(EnvironmentReadFailureCode code, int sourceId) =>
        new(code, sourceId);

    private readonly record struct PassResult(EnvironmentReadFailureCode? Code, int Assignments);

    private sealed class SensitiveByteBuffer(Action<Array>? clearedBufferObserver) : IDisposable
    {
        private byte[] buffer = new byte[256];
        private int length;

        internal int Length => length;

        internal void Append(byte value)
        {
            if (length == buffer.Length)
            {
                var replacement = new byte[checked(buffer.Length * 2)];
                buffer.CopyTo(replacement, 0);
                CryptographicOperations.ZeroMemory(buffer);
                clearedBufferObserver?.Invoke(buffer);
                buffer = replacement;
            }

            buffer[length++] = value;
        }

        internal SensitiveCharacters DecodeUtf8()
        {
            var characters = new char[Encoding.UTF8.GetCharCount(buffer.AsSpan(0, length))];
            _ = Encoding.UTF8.GetChars(buffer.AsSpan(0, length), characters);
            return new SensitiveCharacters(characters, clearedBufferObserver);
        }

        internal void Truncate(int newLength)
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(newLength, length - newLength));
            clearedBufferObserver?.Invoke(buffer);
            length = newLength;
        }

        internal void Reset() => Truncate(0);

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(buffer);
            clearedBufferObserver?.Invoke(buffer);
            length = 0;
        }
    }

    private sealed class SensitiveCharacters(char[] characters, Action<Array>? clearedBufferObserver) : IDisposable
    {
        internal ReadOnlySpan<char> Span => characters;

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
            clearedBufferObserver?.Invoke(characters);
        }
    }
}
