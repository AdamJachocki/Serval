using Serval.Application.Services;
using Serval.Domain.Services;

namespace Serval.Systemd;

/// <summary>Composes an already acquired and parsed complete source set without performing I/O.</summary>
internal static class SystemdEnvironmentComposer
{
    internal enum Phase { Validation, Contribution, BeforePublication }

    internal delegate ServiceEnvironmentReadResult.Success ResultFactory(
        SystemServiceId canonicalServiceId,
        IEnumerable<EnvironmentSourceMetadata> sources,
        IEnumerable<EnvironmentVariableMetadata> variables,
        EnvironmentValues values);

    internal static ServiceEnvironmentReadResult Compose(
        SystemdEnvironmentSourceReadResult.Success sourceSnapshot,
        IReadOnlyList<EnvironmentFileParseResult.Success?> fileCandidates,
        CancellationToken cancellationToken) =>
        Compose(sourceSnapshot, fileCandidates, null, null, null, cancellationToken);

    internal static ServiceEnvironmentReadResult Compose(
        SystemdEnvironmentSourceReadResult.Success sourceSnapshot,
        IReadOnlyList<EnvironmentFileParseResult.Success?> fileCandidates,
        Action<Phase, int>? progressObserver,
        Action<char[]>? clearedOutputBufferObserver,
        ResultFactory? resultFactory,
        CancellationToken cancellationToken)
    {
        EnvironmentValues? output = null;
        var published = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(sourceSnapshot);
            ArgumentNullException.ThrowIfNull(fileCandidates);

            ValidateCompleteInput(sourceSnapshot, fileCandidates, progressObserver, cancellationToken);
            var limitFailure = CheckLimits(sourceSnapshot, fileCandidates, cancellationToken);
            if (limitFailure is not null)
                return limitFailure;

            var sources = BuildSourceMetadata(sourceSnapshot);
            var winningSources = new Dictionary<string, int>(StringComparer.Ordinal);
            output = new EnvironmentValues([], clearedOutputBufferObserver);

            ApplyContribution(sourceSnapshot.ManagerSource.Values,
                sourceSnapshot.ManagerSource.Variables, 0, output, winningSources,
                progressObserver, cancellationToken);
            for (var index = 0; index < sourceSnapshot.FileSources.Count; index++)
            {
                var candidate = fileCandidates[index];
                if (candidate is null)
                    continue;
                ApplyContribution(candidate.Values, candidate.Variables, candidate.SourceId,
                    output, winningSources, progressObserver, cancellationToken);
            }

            var variables = winningSources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new EnvironmentVariableMetadata(pair.Key, pair.Value)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            progressObserver?.Invoke(Phase.BeforePublication, variables.Length);
            cancellationToken.ThrowIfCancellationRequested();

            var result = (resultFactory ?? CreateResult)(
                sourceSnapshot.CanonicalServiceId,
                Array.AsReadOnly(sources),
                Array.AsReadOnly(variables),
                output);
            if (result is null || !ReferenceEquals(result.Values, output))
            {
                result?.Dispose();
                throw new InvalidOperationException("Environment result construction failed.");
            }
            published = true;
            return result;
        }
        finally
        {
            if (!published)
                output?.Dispose();
            DisposeCandidates(fileCandidates);
            sourceSnapshot?.Dispose();
        }
    }

    private static void ValidateCompleteInput(
        SystemdEnvironmentSourceReadResult.Success snapshot,
        IReadOnlyList<EnvironmentFileParseResult.Success?> candidates,
        Action<Phase, int>? progressObserver,
        CancellationToken cancellationToken)
    {
        if (candidates.Count != snapshot.FileSources.Count)
            throw InvalidInput();
        if (snapshot.ManagerSource.Source.Id != 0 ||
            snapshot.ManagerSource.Source.Kind != EnvironmentSourceKind.ManagerEnvironment ||
            snapshot.ManagerSource.Source.IsOptional || snapshot.ManagerSource.Source.IsMissing ||
            snapshot.ManagerSource.SourceBytes < 0 || snapshot.ManagerSource.Assignments < 0 ||
            !HasConsistentValues(snapshot.ManagerSource.Values, snapshot.ManagerSource.Variables,
                snapshot.ManagerSource.Assignments, 0))
            throw InvalidInput();

        var distinct = new HashSet<EnvironmentFileParseResult.Success>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < snapshot.FileSources.Count; index++)
        {
            Observe(Phase.Validation, index, progressObserver, cancellationToken);
            var source = snapshot.FileSources[index];
            var candidate = candidates[index];
            if (source.SourceId != index + 1 || source.IsMissing)
            {
                if (source.SourceId != index + 1 || !source.IsOptional || candidate is not null)
                    throw InvalidInput();
                continue;
            }

            if (candidate is null || !distinct.Add(candidate) || candidate.SourceId != source.SourceId ||
                candidate.SourceBytes != source.Reveal().Length || candidate.Assignments < 0 ||
                !HasConsistentValues(candidate.Values, candidate.Variables,
                    candidate.Assignments, source.SourceId))
                throw InvalidInput();
        }
    }

    private static bool HasConsistentValues(EnvironmentValues values,
        IReadOnlyList<EnvironmentVariableMetadata> variables, int assignments, int sourceId)
    {
        if (values is null || variables is null || assignments < variables.Count ||
            variables.Any(variable => variable is null) ||
            variables.Any(variable => variable.WinningSourceId != sourceId) ||
            variables.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count() != variables.Count)
            return false;
        try
        {
            return values.HasExactly(variables.Select(variable => variable.Name));
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static ServiceEnvironmentReadResult.Failure? CheckLimits(
        SystemdEnvironmentSourceReadResult.Success snapshot,
        IReadOnlyList<EnvironmentFileParseResult.Success?> candidates,
        CancellationToken cancellationToken)
    {
        if (snapshot.FileSources.Count + 1 > EnvironmentReadLimits.MaxSources)
            return Limit(EnvironmentReadLimits.MaxSources);

        long bytes = snapshot.ManagerSource.SourceBytes;
        long assignments = snapshot.ManagerSource.Assignments;
        if (bytes > EnvironmentReadLimits.MaxTotalSourceBytes ||
            snapshot.ManagerSource.SourceBytes > EnvironmentReadLimits.MaxSourceBytes ||
            assignments > EnvironmentReadLimits.MaxAssignments)
            return Limit(0);

        for (var index = 0; index < snapshot.FileSources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            if (candidate is null)
                continue;
            bytes += candidate.SourceBytes;
            assignments += candidate.Assignments;
            if (bytes > EnvironmentReadLimits.MaxTotalSourceBytes ||
                assignments > EnvironmentReadLimits.MaxAssignments)
                return Limit(candidate.SourceId);
        }

        return null;
    }

    private static EnvironmentSourceMetadata[] BuildSourceMetadata(
        SystemdEnvironmentSourceReadResult.Success snapshot)
    {
        var sources = new EnvironmentSourceMetadata[snapshot.FileSources.Count + 1];
        sources[0] = new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment);
        for (var index = 0; index < snapshot.FileSources.Count; index++)
        {
            var source = snapshot.FileSources[index];
            sources[index + 1] = new EnvironmentSourceMetadata(
                source.SourceId, EnvironmentSourceKind.EnvironmentFile, source.IsOptional, source.IsMissing);
        }
        return sources;
    }

    private static void ApplyContribution(EnvironmentValues candidateValues,
        IReadOnlyList<EnvironmentVariableMetadata> variables, int sourceId,
        EnvironmentValues output, Dictionary<string, int> winningSources,
        Action<Phase, int>? progressObserver, CancellationToken cancellationToken)
    {
        foreach (var variable in variables)
        {
            Observe(Phase.Contribution, sourceId, progressObserver, cancellationToken);
            output.Set(variable.Name, candidateValues.Reveal(variable.Name));
            winningSources[variable.Name] = sourceId;
        }
    }

    private static void Observe(Phase phase, int value, Action<Phase, int>? progressObserver,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progressObserver?.Invoke(phase, value);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static ServiceEnvironmentReadResult.Success CreateResult(
        SystemServiceId canonicalServiceId,
        IEnumerable<EnvironmentSourceMetadata> sources,
        IEnumerable<EnvironmentVariableMetadata> variables,
        EnvironmentValues values) => new(canonicalServiceId, sources, variables, values);

    private static ServiceEnvironmentReadResult.Failure Limit(int sourceId) =>
        new(EnvironmentReadFailureCode.LimitExceeded, sourceId: sourceId);

    private static ArgumentException InvalidInput() =>
        new("Invalid environment composition input.");

    private static void DisposeCandidates(IReadOnlyList<EnvironmentFileParseResult.Success?>? candidates)
    {
        if (candidates is null)
            return;
        for (var index = candidates.Count - 1; index >= 0; index--)
            candidates[index]?.Dispose();
    }
}
