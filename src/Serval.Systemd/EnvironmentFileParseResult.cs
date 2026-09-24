using Serval.Application.Services;

namespace Serval.Systemd;

/// <summary>Internal candidate for one declared file; declaration metadata is composed later.</summary>
internal abstract class EnvironmentFileParseResult
{
    private EnvironmentFileParseResult() { }

    internal sealed class Success : EnvironmentFileParseResult, IDisposable
    {
        internal Success(int sourceId, EnvironmentValues values,
            IReadOnlyList<EnvironmentVariableMetadata> variables, int sourceBytes, int assignments)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(sourceId, 1);
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(variables);
            ArgumentOutOfRangeException.ThrowIfNegative(sourceBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(assignments);
            SourceId = sourceId;
            Values = values;
            Variables = variables;
            SourceBytes = sourceBytes;
            Assignments = assignments;
        }

        public int SourceId { get; }
        internal EnvironmentValues Values { get; }
        public IReadOnlyList<EnvironmentVariableMetadata> Variables { get; }
        public int SourceBytes { get; }
        public int Assignments { get; }

        public void Dispose() => Values.Dispose();

        public override string ToString() => "[REDACTED]";
    }

    internal sealed class Failure : EnvironmentFileParseResult
    {
        internal Failure(EnvironmentReadFailureCode code, int sourceId)
        {
            if (code is not (EnvironmentReadFailureCode.InvalidSource or EnvironmentReadFailureCode.LimitExceeded))
                throw new ArgumentOutOfRangeException(nameof(code));
            ArgumentOutOfRangeException.ThrowIfLessThan(sourceId, 1);
            Code = code;
            SourceId = sourceId;
        }

        public EnvironmentReadFailureCode Code { get; }
        public int SourceId { get; }

        public override string ToString() => $"{nameof(EnvironmentFileParseResult)}.{nameof(Failure)}({Code}, {SourceId})";
    }
}
