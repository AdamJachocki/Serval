using Serval.Domain.Services;

namespace Serval.Application.Services;

/// <summary>A complete supported-declarations snapshot or a value-free failure.</summary>
public abstract class ServiceEnvironmentReadResult
{
    private ServiceEnvironmentReadResult() { }

    public sealed class Success : ServiceEnvironmentReadResult
    {
        public Success(SystemServiceId canonicalServiceId,
            IEnumerable<EnvironmentSourceMetadata> sources,
            IEnumerable<EnvironmentVariableMetadata> variables,
            EnvironmentValues values)
        {
            ArgumentNullException.ThrowIfNull(canonicalServiceId);
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(variables);
            ArgumentNullException.ThrowIfNull(values);
            var sourceArray = sources.ToArray();
            var variableArray = variables.ToArray();
            if (sourceArray.Any(source => source is null) ||
                sourceArray.Select(source => source.Id).Distinct().Count() != sourceArray.Length ||
                variableArray.Any(variable => variable is null) ||
                variableArray.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count() != variableArray.Length)
            {
                throw new ArgumentException("Invalid environment metadata.");
            }

            var sourceIds = sourceArray.Where(source => !source.IsMissing).Select(source => source.Id).ToHashSet();
            if (variableArray.Any(variable => !sourceIds.Contains(variable.WinningSourceId)) ||
                !values.HasExactly(variableArray.Select(variable => variable.Name)))
            {
                throw new ArgumentException("Environment metadata and values do not match.");
            }

            CanonicalServiceId = canonicalServiceId;
            Sources = Array.AsReadOnly(sourceArray);
            Variables = Array.AsReadOnly(variableArray);
            Values = values;
        }

        public SystemServiceId CanonicalServiceId { get; }
        public EnvironmentModelScope Scope { get; } = EnvironmentModelScope.SupportedDeclarations;
        public IReadOnlyList<EnvironmentSourceMetadata> Sources { get; }
        public IReadOnlyList<EnvironmentVariableMetadata> Variables { get; }

        [System.Text.Json.Serialization.JsonIgnore]
        public EnvironmentValues Values { get; }
    }

    public sealed class Failure : ServiceEnvironmentReadResult
    {
        public Failure(EnvironmentReadFailureCode code, EnvironmentUnsupportedReason? reason = null, int? sourceId = null)
        {
            if (!Enum.IsDefined(code))
                throw new ArgumentOutOfRangeException(nameof(code));
            if ((code == EnvironmentReadFailureCode.UnsupportedConfiguration) != reason.HasValue ||
                (reason.HasValue && !Enum.IsDefined(reason.Value)))
                throw new ArgumentException("Unsupported configuration requires a defined reason.", nameof(reason));
            if (sourceId < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceId));

            Code = code;
            Reason = reason;
            SourceId = sourceId;
        }

        public EnvironmentReadFailureCode Code { get; }
        public EnvironmentUnsupportedReason? Reason { get; }
        public int? SourceId { get; }
    }
}

public enum EnvironmentModelScope { SupportedDeclarations }

public enum EnvironmentReadFailureCode
{
    NotFound, ProtectedTarget, UnsupportedConfiguration, InvalidSource, SourceUnavailable,
    InconsistentSnapshot, LimitExceeded, Timeout, TransportError,
}

public enum EnvironmentUnsupportedReason
{
    UnsetEnvironment, PassEnvironment, PathPattern, UnresolvedSpecifier,
    TransientUnit, GeneratedUnit, UnsafePath, UnsupportedProperty,
}
