using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdServiceEnvironmentReaderTests
{
    [Fact]
    public async Task ApplicationConsumerReceivesCompleteMaskedResultAndAliasIdentity()
    {
        var scenario = new Scenario(
            "canonical.service",
            ["alias.service", "canonical.service"],
            ["MANAGER=base", "CONFLICT=manager"],
            [
                new SystemdEnvironmentFile("/etc/one.env", false),
                new SystemdEnvironmentFile("/etc/missing.env", true),
                new SystemdEnvironmentFile("/etc/one.env", false),
            ]);
        var files = new TestFiles(new Dictionary<string, byte[]>
        {
            ["/etc/one.env"] = Encoding.UTF8.GetBytes("CONFLICT=file\nEMPTY=\nFILE=value\n"),
        });
        ISystemServiceEnvironmentReader reader = Create(_ => scenario, files);

        var result = await ConsumeAsync(
            reader, new SystemServiceId("alias.service"), TestContext.Current.CancellationToken);
        var success = Assert.IsType<ServiceEnvironmentReadResult.Success>(result);

        Assert.Equal("canonical.service", success.CanonicalServiceId.Value);
        Assert.Equal(EnvironmentModelScope.SupportedDeclarations, success.Scope);
        Assert.Equal([0, 1, 2, 3], success.Sources.Select(source => source.Id));
        Assert.True(success.Sources[2].IsOptional && success.Sources[2].IsMissing);
        Assert.Equal(["CONFLICT", "EMPTY", "FILE", "MANAGER"],
            success.Variables.Select(variable => variable.Name));
        Assert.Equal(3, Winner(success, "CONFLICT"));
        Assert.Equal(0, Winner(success, "MANAGER"));
        Assert.Equal("file", success.Values.Reveal("CONFLICT").ToString());
        var json = JsonSerializer.Serialize<ServiceEnvironmentReadResult>(success);
        Assert.DoesNotContain("base", json, StringComparison.Ordinal);
        Assert.DoesNotContain("file", json, StringComparison.OrdinalIgnoreCase);

        success.Dispose();
        Assert.Equal("CONFLICT", success.Variables[0].Name);
        Assert.Throws<ObjectDisposedException>(() => success.Values.Reveal("CONFLICT"));
        Assert.All(files.ClearedBuffers, AssertCleared);
    }

    [Fact]
    public async Task EmptyDeclarationsAndAliasProduceSuccessfulEmptyModel()
    {
        var scenario = new Scenario(
            "empty.service", ["empty-alias.service", "empty.service"], [], []);
        ISystemServiceEnvironmentReader reader = Create(
            _ => scenario, new TestFiles(new Dictionary<string, byte[]>()));

        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await ConsumeAsync(reader, new SystemServiceId("empty-alias.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal("empty.service", result.CanonicalServiceId.Value);
        Assert.Single(result.Sources);
        Assert.Empty(result.Variables);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task CountsAssignmentsBeforeDeduplicationAcrossSources(
        int fileAssignments,
        bool expectedSuccess)
    {
        var manager = Enumerable.Range(0, EnvironmentReadLimits.MaxAssignments - 1)
            .Select(index => $"V{index}=manager").ToArray();
        var fileText = string.Concat(Enumerable.Repeat("V0=override\n", fileAssignments));
        var scenario = new Scenario(
            "limit.service", ["limit.service"], manager,
            [new SystemdEnvironmentFile("/etc/limit.env", false)]);
        var reader = Create(_ => scenario, new TestFiles(new Dictionary<string, byte[]>
        {
            ["/etc/limit.env"] = Encoding.UTF8.GetBytes(fileText),
        }));

        var result = await reader.ReadAsync(
            new SystemServiceId("limit.service"), TestContext.Current.CancellationToken);

        if (expectedSuccess)
        {
            using var success = Assert.IsType<ServiceEnvironmentReadResult.Success>(result);
            Assert.Equal(1, Winner(success, "V0"));
        }
        else
        {
            var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(result);
            Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, failure.Code);
            Assert.Equal(1, failure.SourceId);
        }
    }

    [Theory]
    [InlineData("invalid", EnvironmentReadFailureCode.InvalidSource, null, 1)]
    [InlineData("missing", EnvironmentReadFailureCode.SourceUnavailable, null, 1)]
    [InlineData("unsupported", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.UnsetEnvironment, null)]
    [InlineData("transport", EnvironmentReadFailureCode.TransportError, null, null)]
    public async Task PreservesSafeComponentFailures(
        string kind,
        EnvironmentReadFailureCode code,
        EnvironmentUnsupportedReason? reason,
        int? sourceId)
    {
        var scenario = new Scenario(
            "failure.service", ["failure.service"], [],
            [new SystemdEnvironmentFile("/private/secret.env", false)]);
        if (kind == "unsupported")
            scenario = scenario with { UnsetEnvironment = ["SECRET"] };
        var files = kind switch
        {
            "invalid" => new TestFiles(new Dictionary<string, byte[]>
            { ["/private/secret.env"] = [0xff] }),
            _ => new TestFiles(new Dictionary<string, byte[]>()),
        };
        SystemdEnvironmentTransportFactory factory = kind == "transport"
            ? (_, _) => throw new SystemdDbusException(SystemdDbusFailureKind.Unavailable)
            : (_, _) => Task.FromResult<ISystemdDbusTransport>(new TestTransport(_ => scenario));
        var reader = new SystemdServiceEnvironmentReader(factory, files, TimeProvider.System);

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("failure.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(code, failure.Code);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(sourceId, failure.SourceId);
        Assert.DoesNotContain("/private", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(failure), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("properties")]
    [InlineData("binding")]
    [InlineData("optional-appearance")]
    public async Task RejectsEveryFinalConsistencyChangeAfterComposition(string kind)
    {
        var scenario = new Scenario(
            "change.service", ["change.service"], ["A=manager"],
            [new SystemdEnvironmentFile("/etc/change.env", kind == "optional-appearance")])
        {
            MutateFinalProperties = kind == "properties",
        };
        var contents = kind == "optional-appearance"
            ? new Dictionary<string, byte[]>()
            : new Dictionary<string, byte[]> { ["/etc/change.env"] = Encoding.UTF8.GetBytes("A=file\n") };
        var rawSourceWasClearedBeforeValidation = false;
        TestFiles? files = null;
        files = new TestFiles(contents)
        {
            Stable = observation => kind == "binding" && observation.Path == "/etc/change.env"
                ? false
                : kind != "optional-appearance" || observation.Path != "/etc/change.env",
            OnStable = observation =>
            {
                if (observation.Path == "/etc/change.env")
                {
                    rawSourceWasClearedBeforeValidation =
                        !files!.ClearedBuffers.IsEmpty &&
                        files.ClearedBuffers.All(buffer => buffer.All(value => value == 0));
                }
            },
        };
        var cleared = new List<char[]>();
        var reader = Create(_ => scenario, files, clearedOutputBufferObserver: cleared.Add);

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("change.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.NotEmpty(cleared);
        Assert.All(cleared, AssertCleared);
        Assert.All(files.ClearedBuffers, AssertCleared);
        if (kind == "binding")
            Assert.True(rawSourceWasClearedBeforeValidation);
    }

    [Fact]
    public async Task CleanupFailurePreventsPublicationAndClearsProvisionalValues()
    {
        var scenario = new Scenario(
            "cleanup.service", ["cleanup.service"], ["A=manager"], []);
        var cleared = new List<char[]>();
        var reader = new SystemdServiceEnvironmentReader(
            (_, _) => Task.FromResult<ISystemdDbusTransport>(new TestTransport(_ => scenario)
            {
                FailDisposal = true,
            }),
            new TestFiles(new Dictionary<string, byte[]>()), TimeProvider.System,
            clearedOutputBufferObserver: cleared.Add);

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("cleanup.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.TransportError, failure.Code);
        Assert.NotEmpty(cleared);
        Assert.All(cleared, AssertCleared);
    }

    [Theory]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeConnection)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeResolution)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeEnvironmentAcquisition)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeFileObservation)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeParsing)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeComposition)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeFinalValidation)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeCleanup)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforePublication)]
    public async Task CallerCancellationWinsAtEveryControlledStage(
        int cancellationStageValue)
    {
        var cancellationStage = (SystemdEnvironmentReadStage)cancellationStageValue;
        using var caller = new CancellationTokenSource();
        var scenario = new Scenario(
            "cancel.service", ["cancel.service"], ["A=manager"],
            [new SystemdEnvironmentFile("/etc/cancel.env", false)]);
        var files = new TestFiles(new Dictionary<string, byte[]>
        { ["/etc/cancel.env"] = Encoding.UTF8.GetBytes("B=file\n") });
        var reader = Create(_ => scenario, files,
            stageObserver: stage =>
            {
                if (stage == cancellationStage)
                    caller.Cancel();
            });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(new SystemServiceId("cancel.service"), caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.All(files.ClearedBuffers, AssertCleared);
    }

    [Theory]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeConnection)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeResolution)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeFileObservation)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeParsing)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeComposition)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeFinalValidation)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforeCleanup)]
    [InlineData((int)SystemdEnvironmentReadStage.BeforePublication)]
    public async Task DelayedTimerStillTimesOutAtEveryControlledStage(
        int deadlineStageValue)
    {
        var deadlineStage = (SystemdEnvironmentReadStage)deadlineStageValue;
        var time = new SystemdEnvironmentOperationContextTests.ControlledTimeProvider
        {
            DeliverTimers = false,
        };
        var scenario = new Scenario(
            "timeout.service", ["timeout.service"], ["A=manager"],
            [new SystemdEnvironmentFile("/etc/timeout.env", false)]);
        var reader = Create(_ => scenario,
            new TestFiles(new Dictionary<string, byte[]>
            { ["/etc/timeout.env"] = Encoding.UTF8.GetBytes("B=file\n") }),
            time,
            stageObserver: stage =>
            {
                if (stage == deadlineStage)
                    time.Advance(EnvironmentReadLimits.OperationDeadline);
            });

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("timeout.service"), CancellationToken.None));

        Assert.Equal(EnvironmentReadFailureCode.Timeout, failure.Code);
    }

    [Fact]
    public async Task PassesOnlyRemainingBudgetToTransport()
    {
        var time = new SystemdEnvironmentOperationContextTests.ControlledTimeProvider
        {
            DeliverTimers = false,
        };
        var scenario = new Scenario("budget.service", ["budget.service"], [], []);
        TimeSpan? allowance = null;
        var reader = new SystemdServiceEnvironmentReader(
            (remaining, _) =>
            {
                allowance = remaining;
                return Task.FromResult<ISystemdDbusTransport>(new TestTransport(_ => scenario));
            },
            new TestFiles(new Dictionary<string, byte[]>()), time,
            stage =>
            {
                if (stage == SystemdEnvironmentReadStage.BeforeConnection)
                    time.Advance(TimeSpan.FromSeconds(2));
            });

        using var result = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await reader.ReadAsync(new SystemServiceId("budget.service"), CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(3), allowance);
    }

    [Theory]
    [InlineData("parser")]
    [InlineData("composer")]
    public async Task MidStageCancellationUsesOriginalTokenAndClearsUnpublishedInputs(string stage)
    {
        using var caller = new CancellationTokenSource();
        var scenario = new Scenario(
            "midstage.service", ["midstage.service"], ["A=manager"],
            [new SystemdEnvironmentFile("/etc/midstage.env", false)]);
        var files = new TestFiles(new Dictionary<string, byte[]>
        {
            ["/etc/midstage.env"] = Encoding.UTF8.GetBytes("B=file\nC=other\n"),
        });
        var reader = new SystemdServiceEnvironmentReader(
            (_, _) => Task.FromResult<ISystemdDbusTransport>(
                new TestTransport(_ => scenario)),
            files,
            TimeProvider.System,
            parserProgressObserver: stage == "parser" ? (_, _) => caller.Cancel() : null,
            composerProgressObserver: stage == "composer" ? (_, _) => caller.Cancel() : null);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(new SystemServiceId("midstage.service"), caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.NotEmpty(files.ClearedBuffers);
        Assert.All(files.ClearedBuffers, AssertCleared);
    }

    [Fact]
    public async Task ConcurrentAndRepeatedReadsAreIsolatedAndNeverCached()
    {
        var scenarios = new Dictionary<string, Scenario>(StringComparer.Ordinal)
        {
            ["first.service"] = new("first.service", ["first.service"], ["SHARED=first"], []),
            ["second.service"] = new("second.service", ["second.service"], ["SHARED=second"], []),
        };
        var connections = 0;
        var reader = new SystemdServiceEnvironmentReader(
            (_, _) =>
            {
                Interlocked.Increment(ref connections);
                return Task.FromResult<ISystemdDbusTransport>(
                    new TestTransport(name => scenarios[name]));
            },
            new TestFiles(new Dictionary<string, byte[]>()), TimeProvider.System);

        var firstRead = reader.ReadAsync(
            new SystemServiceId("first.service"), TestContext.Current.CancellationToken);
        var secondRead = reader.ReadAsync(
            new SystemServiceId("second.service"), TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(firstRead, secondRead);
        var first = Assert.IsType<ServiceEnvironmentReadResult.Success>(results[0]);
        using var second = Assert.IsType<ServiceEnvironmentReadResult.Success>(results[1]);
        Assert.Equal("first", first.Values.Reveal("SHARED").ToString());
        Assert.Equal("second", second.Values.Reveal("SHARED").ToString());
        first.Dispose();
        Assert.Equal("second", second.Values.Reveal("SHARED").ToString());

        scenarios["first.service"] = scenarios["first.service"] with
        {
            ManagerEnvironment = ["SHARED=changed"],
        };
        using var repeated = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await reader.ReadAsync(new SystemServiceId("first.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal("changed", repeated.Values.Reveal("SHARED").ToString());
        Assert.Equal(3, connections);
    }

    [Fact]
    public async Task DirectProtectedAliasIsDeniedBeforeSensitiveProperties()
    {
        TestTransport? transport = null;
        var protectedScenario = new Scenario(
            "systemd-journald.service",
            ["ordinary-alias.service", "systemd-journald.service"], [], []);
        var reader = new SystemdServiceEnvironmentReader(
            (_, _) =>
            {
                transport = new TestTransport(_ => protectedScenario);
                return Task.FromResult<ISystemdDbusTransport>(transport);
            },
            new TestFiles(new Dictionary<string, byte[]>()), TimeProvider.System);

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("ordinary-alias.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, failure.Code);
        Assert.Equal(0, transport?.EnvironmentReads);
    }

    [Fact]
    public async Task EveryApplicationConstructionPathEnforcesIdentityAndProtectionBeforeSensitiveAccess()
    {
        var connections = 0;
        var directReader = new SystemdServiceEnvironmentReader(
            (_, _) =>
            {
                connections++;
                throw new InvalidOperationException();
            },
            new TestFiles(new Dictionary<string, byte[]>()), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => directReader.ReadAsync(
            new SystemServiceId("worker@.service"), TestContext.Current.CancellationToken));
        var builtIn = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await directReader.ReadAsync(new SystemServiceId("systemd-journald.service"),
                TestContext.Current.CancellationToken));
        var privileged = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await directReader.ReadAsync(new SystemServiceId("serval-agent@sample.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, builtIn.Code);
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, privileged.Code);
        Assert.Equal(0, connections);

        var lookalike = new Scenario(
            "serval-agent-helper@sample.service",
            ["serval-agent-helper@sample.service"], [], []);
        var reader = new SystemdServiceEnvironmentReader(
            (_, _) => Task.FromResult<ISystemdDbusTransport>(
                new TestTransport(name => name == "missing.service" ? null : lookalike)),
            new TestFiles(new Dictionary<string, byte[]>()), TimeProvider.System);
        var notFound = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(new SystemServiceId("missing.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.NotFound, notFound.Code);
        using var permitted = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await reader.ReadAsync(new SystemServiceId("serval-agent-helper@sample.service"),
                TestContext.Current.CancellationToken));
    }

    private static Task<ServiceEnvironmentReadResult> ConsumeAsync(
        ISystemServiceEnvironmentReader reader,
        SystemServiceId serviceId,
        CancellationToken cancellationToken) => reader.ReadAsync(serviceId, cancellationToken);

    private static SystemdServiceEnvironmentReader Create(
        Func<string, Scenario> scenario,
        TestFiles files,
        TimeProvider? timeProvider = null,
        Action<SystemdEnvironmentReadStage>? stageObserver = null,
        Action<char[]>? clearedOutputBufferObserver = null) =>
        new((_, _) => Task.FromResult<ISystemdDbusTransport>(new TestTransport(scenario)),
            files, timeProvider ?? TimeProvider.System, stageObserver,
            clearedOutputBufferObserver: clearedOutputBufferObserver);

    private static int Winner(ServiceEnvironmentReadResult.Success result, string name) =>
        Assert.Single(result.Variables, variable => variable.Name == name).WinningSourceId;

    private static void AssertCleared(byte[] buffer) =>
        Assert.All(buffer, value => Assert.Equal(0, value));

    private static void AssertCleared(char[] buffer) =>
        Assert.All(buffer, value => Assert.Equal('\0', value));

    private sealed record Scenario(
        string CanonicalId,
        string[] Names,
        string[] ManagerEnvironment,
        SystemdEnvironmentFile[] EnvironmentFiles)
    {
        internal string[] UnsetEnvironment { get; init; } = [];
        internal bool MutateFinalProperties { get; init; }
    }

    private sealed class TestFiles : ISystemdSourceFileAccess
    {
        private readonly IReadOnlyDictionary<string, byte[]> _contents;
        internal TestFiles(IReadOnlyDictionary<string, byte[]> contents) => _contents = contents;

        internal Func<SystemdFileObservation, bool> Stable { get; init; } = _ => true;
        internal Action<SystemdFileObservation>? OnStable { get; init; }
        internal ConcurrentBag<byte[]> ClearedBuffers { get; } = [];

        public ValueTask<SystemdFileObservation> ObserveAsync(
            string path,
            bool readContent,
            bool allowMissing,
            CancellationToken cancellationToken,
            int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!readContent)
                return ValueTask.FromResult(new SystemdFileObservation(path, new ClearableBytes()));
            if (!_contents.TryGetValue(path, out var bytes))
            {
                if (allowMissing)
                    return ValueTask.FromResult(new SystemdFileObservation(path, missing: true));
                throw new SystemdSourceFileException(SystemdSourceFileFailure.Missing);
            }
            if (bytes.Length > maximumContentBytes)
                throw new SystemdSourceFileException(SystemdSourceFileFailure.LimitExceeded);
            var content = new ClearableBytes(clearedBufferObserver: ClearedBuffers.Add);
            content.Append(bytes);
            return ValueTask.FromResult(new SystemdFileObservation(path, content));
        }

        public ValueTask<bool> IsStableAsync(
            SystemdFileObservation observation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnStable?.Invoke(observation);
            return ValueTask.FromResult(Stable(observation));
        }
    }

    private sealed class TestTransport(Func<string, Scenario?> getScenario) : ISystemdDbusTransport
    {
        private readonly SystemdUnitReference _unit = new();
        private Scenario? _scenario;

        internal bool FailDisposal { get; init; }
        internal int EnvironmentReads { get; private set; }

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult("255");

        public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
            IReadOnlyCollection<SystemServiceId> serviceIds,
            CancellationToken cancellationToken)
        {
            var requested = Assert.Single(serviceIds).Value;
            _scenario = getScenario(requested);
            if (_scenario is null)
                return Task.FromResult<IReadOnlyList<SystemdListedUnit>>([]);
            return Task.FromResult<IReadOnlyList<SystemdListedUnit>>(
                [new SystemdListedUnit(_scenario.CanonicalId, "description", "loaded",
                    "inactive", "dead", string.Empty, _unit)]);
        }

        public Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken)
        {
            var scenario = _scenario ?? throw new InvalidOperationException();
            return Task.FromResult(new SystemdUnitProperties(
                scenario.CanonicalId, scenario.Names, "description",
                "loaded", "inactive", "dead"));
        }

        public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken)
        {
            var scenario = _scenario ?? throw new InvalidOperationException();
            EnvironmentReads++;
            return Task.FromResult(new SystemdEnvironmentProperties(
                scenario.CanonicalId,
                scenario.Names,
                "loaded",
                "/etc/systemd/system/" + scenario.CanonicalId,
                [],
                false,
                false,
                scenario.MutateFinalProperties && EnvironmentReads == 2 ? "disabled" : "enabled",
                scenario.ManagerEnvironment,
                scenario.EnvironmentFiles,
                scenario.UnsetEnvironment,
                []));
        }

        public ValueTask DisposeAsync()
        {
            if (FailDisposal)
                throw new SystemdDbusException(SystemdDbusFailureKind.Unavailable);
            return ValueTask.CompletedTask;
        }
    }
}
