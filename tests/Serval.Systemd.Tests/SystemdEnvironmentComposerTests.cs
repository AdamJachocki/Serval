using System.Text;
using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdEnvironmentComposerTests
{
    [Fact]
    public void ComposesManagerPresentAndOptionalMissingSourcesAndTransfersOwnership()
    {
        var (snapshot, candidates) = CreateInput(
            ["MANAGER=manager"],
            new(false, "FILE=file"),
            new(true, null));

        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            SystemdEnvironmentComposer.Compose(snapshot, candidates, TestContext.Current.CancellationToken));

        Assert.Equal("example.service", result.CanonicalServiceId.Value);
        Assert.Equal([0, 1, 2], result.Sources.Select(source => source.Id));
        Assert.Equal([false, false, true], result.Sources.Select(source => source.IsOptional));
        Assert.Equal([false, false, true], result.Sources.Select(source => source.IsMissing));
        Assert.Equal(["FILE", "MANAGER"], result.Variables.Select(variable => variable.Name));
        Equal(result, "MANAGER", "manager", 0);
        Equal(result, "FILE", "file", 1);
        Assert.Throws<ObjectDisposedException>(() => snapshot.ManagerSource.Values.Reveal("MANAGER"));
        Assert.Throws<ObjectDisposedException>(() => candidates[0]!.Values.Reveal("FILE"));
        Assert.Throws<ObjectDisposedException>(() => snapshot.FileSources[0].Reveal());
    }

    [Fact]
    public void ComposesEmptyManagerOnlySnapshot()
    {
        var (snapshot, candidates) = CreateInput([]);

        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            SystemdEnvironmentComposer.Compose(snapshot, candidates, TestContext.Current.CancellationToken));

        Assert.Single(result.Sources);
        Assert.Empty(result.Variables);
        Assert.True(result.Values.HasExactly([]));
    }

    [Fact]
    public void AppliesSourcePrecedenceAndPublishesDeterministicProvenance()
    {
        var scenarios = new[]
        {
            new Scenario(["DUP=first", "DUP=final"], [],
                new Dictionary<string, Winner> { ["DUP"] = new("final", 0) }),
            new Scenario(["CONFLICT=manager"], [new(false, "CONFLICT=file")],
                new Dictionary<string, Winner> { ["CONFLICT"] = new("file", 1) }),
            new Scenario([], [new(false, "WIN=first"), new(false, "WIN=second")],
                new Dictionary<string, Winner> { ["WIN"] = new("second", 2) }),
            new Scenario([], [new(false, "REPEAT=value"), new(false, "REPEAT=value")],
                new Dictionary<string, Winner> { ["REPEAT"] = new("value", 2) }),
            new Scenario(["NAME=upper", "name=lower", "EMPTY=not-empty"], [new(false, "EMPTY=")],
                new Dictionary<string, Winner>
                {
                    ["EMPTY"] = new("", 1), ["NAME"] = new("upper", 0), ["name"] = new("lower", 0),
                }),
            new Scenario(["RESET_FINAL=manager"], [new(true, null)],
                new Dictionary<string, Winner> { ["RESET_FINAL"] = new("manager", 0) }),
        };

        foreach (var scenario in scenarios)
        {
            var (snapshot, candidates) = CreateInput(scenario.Manager, scenario.Files);
            using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
                SystemdEnvironmentComposer.Compose(snapshot, candidates, TestContext.Current.CancellationToken));

            Assert.Equal(scenario.Expected.Keys.Order(StringComparer.Ordinal),
                result.Variables.Select(variable => variable.Name));
            foreach (var pair in scenario.Expected)
                Equal(result, pair.Key, pair.Value.Value, pair.Value.SourceId);
        }
    }

    [Fact]
    public void RetainsEveryOccurrenceInSourceOrderWithoutPathOrHistoryMetadata()
    {
        var (snapshot, candidates) = CreateInput(
            ["MANAGER=final"],
            new(false, "REPEATED=value"),
            new(true, null),
            new(false, "LATER=value"),
            new(false, "REPEATED=value"));

        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            SystemdEnvironmentComposer.Compose(snapshot, candidates, TestContext.Current.CancellationToken));

        Assert.Equal(
            [(0, EnvironmentSourceKind.ManagerEnvironment, false, false),
             (1, EnvironmentSourceKind.EnvironmentFile, false, false),
             (2, EnvironmentSourceKind.EnvironmentFile, true, true),
             (3, EnvironmentSourceKind.EnvironmentFile, false, false),
             (4, EnvironmentSourceKind.EnvironmentFile, false, false)],
            result.Sources.Select(source => (source.Id, source.Kind, source.IsOptional, source.IsMissing)));
        Equal(result, "MANAGER", "final", 0);
        Equal(result, "LATER", "value", 3);
        Equal(result, "REPEATED", "value", 4);
        Assert.All(result.Sources, source => Assert.Equal(
            ["Id", "IsMissing", "IsOptional", "Kind"],
            source.GetType().GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void RejectsOmittedExtraSwappedAndDuplicateCandidateSlots()
    {
        var (omittedSnapshot, omittedCandidates) = CreateInput([], new(false, "A=1"), new(false, "B=2"));
        try
        {
            var error = Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
                omittedSnapshot, [omittedCandidates[0]], TestContext.Current.CancellationToken));
            Assert.Equal("Invalid environment composition input.", error.Message);
        }
        finally
        {
            omittedCandidates[1]!.Dispose();
        }

        var (extraSnapshot, extraCandidates) = CreateInput([], new FileSpec(false, "A=1"));
        var extra = Parse("B=2", 2);
        Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
            extraSnapshot, [extraCandidates[0], extra], TestContext.Current.CancellationToken));

        var (swappedSnapshot, swappedCandidates) = CreateInput([], new(false, "A=1"), new(false, "B=2"));
        Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
            swappedSnapshot, [swappedCandidates[1], swappedCandidates[0]], TestContext.Current.CancellationToken));

        var (duplicateSnapshot, duplicateCandidates) = CreateInput([], new(false, "A=1"), new(false, "A=1"));
        try
        {
            Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
                duplicateSnapshot, [duplicateCandidates[0], duplicateCandidates[0]],
                TestContext.Current.CancellationToken));
        }
        finally
        {
            duplicateCandidates[1]!.Dispose();
        }
    }

    [Fact]
    public void RejectsMalformedCandidateIdentityBytesAndMetadataWithoutValuesInDiagnostics()
    {
        var marker = Guid.NewGuid().ToString("N");
        AssertMalformed(marker, sourceId: 2, sourceBytes: Encoding.UTF8.GetByteCount("SECRET=" + marker),
            [new EnvironmentVariableMetadata("SECRET", 2)]);
        AssertMalformed(marker, sourceId: 1, sourceBytes: 0,
            [new EnvironmentVariableMetadata("SECRET", 1)]);
        AssertMalformed(marker, sourceId: 1, sourceBytes: Encoding.UTF8.GetByteCount("SECRET=" + marker),
            [new EnvironmentVariableMetadata("OTHER", 1)]);
        AssertMalformed(marker, sourceId: 1, sourceBytes: Encoding.UTF8.GetByteCount("SECRET=" + marker),
            [new EnvironmentVariableMetadata("SECRET", 2)]);
        AssertMalformed(marker, sourceId: 1, sourceBytes: Encoding.UTF8.GetByteCount("SECRET=" + marker),
            [new EnvironmentVariableMetadata("SECRET", 1)], assignments: 0);
    }

    [Fact]
    public void EnforcesInclusiveSourceAndAssignmentLimits()
    {
        var exactMissing = Enumerable.Range(1, EnvironmentReadLimits.MaxSources - 1)
            .Select(_ => new FileSpec(true, null)).ToArray();
        var (exactSourceSnapshot, exactSourceCandidates) = CreateInput([], exactMissing);
        using (Assert.IsType<ServiceEnvironmentReadResult.Success>(SystemdEnvironmentComposer.Compose(
                   exactSourceSnapshot, exactSourceCandidates, TestContext.Current.CancellationToken))) { }

        var excessMissing = Enumerable.Range(1, EnvironmentReadLimits.MaxSources)
            .Select(_ => new FileSpec(true, null)).ToArray();
        var (excessSourceSnapshot, excessSourceCandidates) = CreateInput([], excessMissing);
        var sourceFailure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(SystemdEnvironmentComposer.Compose(
            excessSourceSnapshot, excessSourceCandidates, TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, sourceFailure.Code);
        Assert.Equal(EnvironmentReadLimits.MaxSources, sourceFailure.SourceId);

        var exactAssignments = Enumerable.Repeat("OVERRIDDEN=value", EnvironmentReadLimits.MaxAssignments).ToArray();
        var (exactAssignmentSnapshot, exactAssignmentCandidates) = CreateInput(exactAssignments);
        using (var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(SystemdEnvironmentComposer.Compose(
                   exactAssignmentSnapshot, exactAssignmentCandidates, TestContext.Current.CancellationToken)))
            Equal(result, "OVERRIDDEN", "value", 0);

        var (excessAssignmentSnapshot, excessAssignmentCandidates) = CreateInput(
            exactAssignments, new FileSpec(false, "OVERRIDDEN=later"));
        var assignmentFailure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            SystemdEnvironmentComposer.Compose(excessAssignmentSnapshot, excessAssignmentCandidates,
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, assignmentFailure.Code);
        Assert.Equal(1, assignmentFailure.SourceId);
    }

    [Fact]
    public void CountsRepeatedOccurrenceBytesAtExactLimitAndFirstExcess()
    {
        var full = new byte[EnvironmentReadLimits.MaxSourceBytes];
        Array.Fill(full, (byte)'\n');
        var exactSources = Enumerable.Range(0, 4).Select(_ => full).ToArray();
        var (exactSnapshot, exactCandidates) = CreateRawInput(exactSources);
        using (Assert.IsType<ServiceEnvironmentReadResult.Success>(SystemdEnvironmentComposer.Compose(
                   exactSnapshot, exactCandidates, TestContext.Current.CancellationToken))) { }

        var excessSources = exactSources.Append([(byte)'\n']).ToArray();
        var (excessSnapshot, excessCandidates) = CreateRawInput(excessSources);
        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(SystemdEnvironmentComposer.Compose(
            excessSnapshot, excessCandidates, TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, failure.Code);
        Assert.Equal(5, failure.SourceId);
    }

    [Fact]
    public void ClearsReplacedAndTransferredBuffersOnSuccess()
    {
        var managerCleared = new List<char[]>();
        var candidateCleared = new List<char[]>();
        var rawCleared = new List<byte[]>();
        var outputCleared = new List<char[]>();
        var managerValues = new EnvironmentValues([new("SECRET", "manager")], managerCleared.Add);
        var manager = new LoadedEnvironmentResult.Success(managerValues,
            [new EnvironmentVariableMetadata("SECRET", 0)], 14, 1);
        var raw = Bytes("SECRET=file", rawCleared.Add);
        var source = new SystemdEnvironmentFileSource(1, false, false, raw);
        var candidateValues = new EnvironmentValues([new("SECRET", "file")], candidateCleared.Add);
        var candidate = new EnvironmentFileParseResult.Success(1, candidateValues,
            [new EnvironmentVariableMetadata("SECRET", 1)], raw.Length, 1);
        var snapshot = new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("example.service"), manager, [source]);

        var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(SystemdEnvironmentComposer.Compose(
            snapshot, [candidate], null, outputCleared.Add, null, TestContext.Current.CancellationToken));
        Assert.All(managerCleared, buffer => AssertCleared(buffer));
        Assert.All(candidateCleared, buffer => AssertCleared(buffer));
        Assert.All(rawCleared, buffer => AssertCleared(buffer));
        Assert.Single(outputCleared);
        Assert.All(outputCleared, buffer => AssertCleared(buffer));
        Equal(result, "SECRET", "file", 1);

        result.Dispose();
        Assert.Equal(2, outputCleared.Count);
        Assert.All(outputCleared, buffer => AssertCleared(buffer));
    }

    [Fact]
    public void ClearsAllOwnedBuffersOnMalformedLimitAndResultConstructionFailures()
    {
        var marker = Guid.NewGuid().ToString("N");
        var malformedCleared = new List<char[]>();
        var (malformedSnapshot, originalCandidates) = CreateInput(
            [], new FileSpec(false, "SECRET=" + marker));
        originalCandidates[0]!.Dispose();
        var malformedValues = new EnvironmentValues([new("SECRET", marker)], malformedCleared.Add);
        var malformed = new EnvironmentFileParseResult.Success(1, malformedValues,
            [new EnvironmentVariableMetadata("OTHER", 1)], Encoding.UTF8.GetByteCount("SECRET=" + marker), 1);
        var malformedError = Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
            malformedSnapshot, [malformed], TestContext.Current.CancellationToken));
        Assert.DoesNotContain(marker, malformedError.ToString(), StringComparison.Ordinal);
        Assert.All(malformedCleared, buffer => AssertCleared(buffer));

        var limitCleared = new List<char[]>();
        var managerValues = new EnvironmentValues([new("SECRET", marker)], limitCleared.Add);
        var overLimitManager = new LoadedEnvironmentResult.Success(managerValues,
            [new EnvironmentVariableMetadata("SECRET", 0)], 1, EnvironmentReadLimits.MaxAssignments + 1);
        var limitSnapshot = new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("example.service"), overLimitManager, []);
        var limit = Assert.IsType<ServiceEnvironmentReadResult.Failure>(SystemdEnvironmentComposer.Compose(
            limitSnapshot, [], TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, limit.Code);
        Assert.All(limitCleared, buffer => AssertCleared(buffer));

        var outputCleared = new List<char[]>();
        var (constructionSnapshot, constructionCandidates) = CreateInput(["SECRET=" + marker]);
        var constructionError = Assert.Throws<InvalidOperationException>(() => SystemdEnvironmentComposer.Compose(
            constructionSnapshot, constructionCandidates, null, outputCleared.Add,
            (_, _, _, _) => throw new InvalidOperationException("Synthetic result construction failure."),
            TestContext.Current.CancellationToken));
        Assert.DoesNotContain(marker, constructionError.ToString(), StringComparison.Ordinal);
        Assert.All(outputCleared, buffer => AssertCleared(buffer));
    }

    [Fact]
    public void PreservesCallerCancellationBeforeDuringAndBeforePublication()
    {
        using (var canceled = new CancellationTokenSource())
        {
            var (snapshot, candidates) = CreateInput(["SECRET=value"]);
            canceled.Cancel();
            var error = Assert.Throws<OperationCanceledException>(() =>
                SystemdEnvironmentComposer.Compose(snapshot, candidates, canceled.Token));
            Assert.Equal(canceled.Token, error.CancellationToken);
            Assert.Throws<ObjectDisposedException>(() => snapshot.ManagerSource.Values.Reveal("SECRET"));
        }

        foreach (var cancelBeforePublication in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var cleared = new List<char[]>();
            var (snapshot, candidates) = CreateInput(["FIRST=value", "SECOND=value"]);
            var contributions = 0;
            var error = Assert.Throws<OperationCanceledException>(() => SystemdEnvironmentComposer.Compose(
                snapshot, candidates, (phase, _) =>
                {
                    if (phase == SystemdEnvironmentComposer.Phase.Contribution && ++contributions == 2 &&
                        !cancelBeforePublication ||
                        phase == SystemdEnvironmentComposer.Phase.BeforePublication && cancelBeforePublication)
                        cancellation.Cancel();
                }, cleared.Add, null, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.All(cleared, buffer => AssertCleared(buffer));
        }
    }

    [Fact]
    public void KeepsValuesOutOfStringSerializationAndFixedDiagnostics()
    {
        var marker = Guid.NewGuid().ToString("N");
        var (snapshot, candidates) = CreateInput(
            ["SECRET=" + marker], new FileSpec(false, "SECRET=" + marker + "-file"));
        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(SystemdEnvironmentComposer.Compose(
            snapshot, candidates, TestContext.Current.CancellationToken));

        var exposed = result + JsonSerializer.Serialize(result) + result.Values + JsonSerializer.Serialize(result.Values);
        Assert.DoesNotContain(marker, exposed, StringComparison.Ordinal);
    }

    private static void AssertMalformed(string marker, int sourceId, int sourceBytes,
        IReadOnlyList<EnvironmentVariableMetadata> variables, int assignments = 1)
    {
        var sourceText = "SECRET=" + marker;
        var raw = Encoding.UTF8.GetBytes(sourceText);
        var manager = Assert.IsType<LoadedEnvironmentResult.Success>(
            LoadedEnvironmentDecoder.Decode([], TestContext.Current.CancellationToken));
        var content = Bytes(raw);
        var snapshot = new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("example.service"), manager,
            [new SystemdEnvironmentFileSource(1, false, false, content)]);
        var candidate = new EnvironmentFileParseResult.Success(sourceId,
            new EnvironmentValues([new("SECRET", marker)]), variables, sourceBytes, assignments);

        var error = Assert.Throws<ArgumentException>(() => SystemdEnvironmentComposer.Compose(
            snapshot, [candidate], TestContext.Current.CancellationToken));
        Assert.Equal("Invalid environment composition input.", error.Message);
        Assert.DoesNotContain(marker, error.ToString(), StringComparison.Ordinal);
    }

    private static (SystemdEnvironmentSourceReadResult.Success Snapshot,
        EnvironmentFileParseResult.Success?[] Candidates) CreateInput(
        string[] managerEntries, params FileSpec[] files)
    {
        var manager = Assert.IsType<LoadedEnvironmentResult.Success>(
            LoadedEnvironmentDecoder.Decode(managerEntries, TestContext.Current.CancellationToken));
        var sources = new List<SystemdEnvironmentFileSource>();
        var candidates = new EnvironmentFileParseResult.Success?[files.Length];
        for (var index = 0; index < files.Length; index++)
        {
            var sourceId = index + 1;
            if (files[index].Content is null)
            {
                sources.Add(new SystemdEnvironmentFileSource(sourceId, files[index].Optional, true, null));
                continue;
            }

            var raw = Encoding.UTF8.GetBytes(files[index].Content!);
            var content = Bytes(raw);
            sources.Add(new SystemdEnvironmentFileSource(sourceId, files[index].Optional, false, content));
            candidates[index] = Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
                raw, sourceId, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken));
        }

        return (new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("example.service"), manager, sources), candidates);
    }

    private static (SystemdEnvironmentSourceReadResult.Success Snapshot,
        EnvironmentFileParseResult.Success?[] Candidates) CreateRawInput(byte[][] sources)
    {
        var manager = Assert.IsType<LoadedEnvironmentResult.Success>(
            LoadedEnvironmentDecoder.Decode([], TestContext.Current.CancellationToken));
        var fileSources = new List<SystemdEnvironmentFileSource>();
        var candidates = new EnvironmentFileParseResult.Success?[sources.Length];
        for (var index = 0; index < sources.Length; index++)
        {
            var sourceId = index + 1;
            var content = Bytes(sources[index]);
            fileSources.Add(new SystemdEnvironmentFileSource(sourceId, false, false, content));
            candidates[index] = new EnvironmentFileParseResult.Success(sourceId,
                new EnvironmentValues([]), [], sources[index].Length, 0);
        }
        return (new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("example.service"), manager, fileSources), candidates);
    }

    private static EnvironmentFileParseResult.Success Parse(string source, int sourceId) =>
        Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
            Encoding.UTF8.GetBytes(source), sourceId, EnvironmentReadLimits.MaxAssignments,
            TestContext.Current.CancellationToken));

    private static ClearableBytes Bytes(string source, Action<byte[]>? observer = null) =>
        Bytes(Encoding.UTF8.GetBytes(source), observer);

    private static ClearableBytes Bytes(byte[] source, Action<byte[]>? observer = null)
    {
        var bytes = new ClearableBytes(source.Length, observer);
        bytes.Append(source);
        return bytes;
    }

    private static void Equal(ServiceEnvironmentReadResult.Success result, string name, string value, int sourceId)
    {
        Assert.True(result.Values.Reveal(name).SequenceEqual(value), name);
        Assert.Equal(sourceId, Assert.Single(result.Variables, variable => variable.Name == name).WinningSourceId);
    }

    private static void AssertCleared(char[] buffer) =>
        Assert.All(buffer, character => Assert.Equal('\0', character));

    private static void AssertCleared(byte[] buffer) =>
        Assert.All(buffer, value => Assert.Equal(0, value));

    private sealed record FileSpec(bool Optional, string? Content);
    private sealed record Winner(string Value, int SourceId);
    private sealed record Scenario(string[] Manager, FileSpec[] Files, Dictionary<string, Winner> Expected);
}
