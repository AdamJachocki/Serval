using System.Text;
using Serval.Application.Services;

namespace Serval.Systemd;

/// <summary>Decodes manager-processed entries, never unit syntax. No I/O or expansion.</summary>
internal static class LoadedEnvironmentDecoder
{
    internal const int MaxSourceBytes = EnvironmentReadLimits.MaxSourceBytes;
    internal const int MaxEntryBytes = EnvironmentReadLimits.MaxLogicalRecordBytes;
    internal const int MaxAssignments = EnvironmentReadLimits.MaxAssignments;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static LoadedEnvironmentResult Decode(ReadOnlySpan<string> entries, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entries.Length > MaxAssignments)
            return Failure(EnvironmentReadFailureCode.LimitExceeded);

        var byteCount = 0;
        // Validate the complete input before retaining any values. Caller owns the input strings.
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null)
                return Failure(EnvironmentReadFailureCode.InvalidSource);
            // UTF-8 requires at least as many bytes as UTF-16 code units for valid input.
            if (entry.Length > MaxEntryBytes)
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            int entryBytes;
            try
            {
                entryBytes = StrictUtf8.GetByteCount(entry);
            }
            catch (EncoderFallbackException)
            {
                return Failure(EnvironmentReadFailureCode.InvalidSource);
            }

            byteCount += entryBytes;
            if (entryBytes > MaxEntryBytes || byteCount > MaxSourceBytes)
                return Failure(EnvironmentReadFailureCode.LimitExceeded);
            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || entry.Contains('\0', StringComparison.Ordinal) || !IsName(entry.AsSpan(0, separator)))
                return Failure(EnvironmentReadFailureCode.InvalidSource);
        }

        var values = new EnvironmentValues([]);
        try
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var separator = entry.IndexOf('=', StringComparison.Ordinal);
                var name = entry[..separator];
                values.Set(name, entry.AsSpan(separator + 1));
                names.Add(name);
            }

            var variables = names.Order(StringComparer.Ordinal)
                .Select(name => new EnvironmentVariableMetadata(name, 0)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new LoadedEnvironmentResult.Success(values, Array.AsReadOnly(variables), byteCount, entries.Length);
        }
        catch
        {
            values.Dispose();
            throw;
        }
    }

    private static bool IsName(ReadOnlySpan<char> name)
    {
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;
        foreach (var character in name)
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_'))
                return false;
        return true;
    }

    private static LoadedEnvironmentResult.Failure Failure(EnvironmentReadFailureCode code) => new(code);
}

/// <summary>Internal candidate, not a complete service snapshot. Dispose after composition.</summary>
internal abstract class LoadedEnvironmentResult
{
    private LoadedEnvironmentResult() { }

    internal sealed class Success(EnvironmentValues values,
        IReadOnlyList<EnvironmentVariableMetadata> variables, int sourceBytes, int assignments)
        : LoadedEnvironmentResult, IDisposable
    {
        internal EnvironmentValues Values { get; } = values;
        public IReadOnlyList<EnvironmentVariableMetadata> Variables { get; } = variables;
        public EnvironmentSourceMetadata Source { get; } = new(0, EnvironmentSourceKind.ManagerEnvironment);
        // Accounting is internal, never part of the metadata projection or default serialization.
        internal int SourceBytes { get; } = sourceBytes;
        internal int Assignments { get; } = assignments;
        public void Dispose() => Values.Dispose();
        public override string ToString() => "[REDACTED]";
    }

    internal sealed class Failure(EnvironmentReadFailureCode code) : LoadedEnvironmentResult
    {
        public EnvironmentReadFailureCode Code { get; } = code;
        public int SourceId { get; }
    }
}
