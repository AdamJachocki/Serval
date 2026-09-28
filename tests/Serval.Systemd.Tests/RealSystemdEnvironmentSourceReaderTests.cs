using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.EnvironmentOracle;
using Serval.Systemd.DBus;
using Serval.Systemd.Tests.DBus;
using Tmds.DBus.Protocol;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdEnvironmentSourceReaderTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact]
    public void RequiredRealSystemdSuiteIsEnabled()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("SERVAL_REQUIRE_REAL_SYSTEMD_TESTS"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(IsEnabled, "real-systemd-suite-disabled");
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task ReadsManagerOrderedRepeatedRuntimeSourcesAndPreservesFixtureBytes()
    {
        var prefix = RequiredPrefix();
        var canonicalName = prefix + "-source@sample.service";
        var aliasName = prefix + "-source-alias@sample.service";
        var sourcePaths = new[]
        {
            "/run/systemd/system/" + prefix + "-source-sample-one.env",
            "/run/systemd/system/" + prefix + "-source-sample-two.env",
            "/run/systemd/system/" + prefix + "-source-sample-three.env",
            "/run/systemd/system/" + prefix + "-source-sample-optional-present.env",
        };
        var before = sourcePaths.ToDictionary(
            path => path,
            path => File.ReadAllBytes(path),
            StringComparer.Ordinal);
        try
        {
            var reader = new SystemdEnvironmentSourceReader();
            using var canonical = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
                await reader.ReadAsync(
                    new SystemServiceId(canonicalName),
                    TestContext.Current.CancellationToken));
            using var alias = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
                await reader.ReadAsync(
                    new SystemServiceId(aliasName),
                    TestContext.Current.CancellationToken));

            Assert.Equal(canonicalName, canonical.CanonicalServiceId.Value);
            Assert.Equal(canonicalName, alias.CanonicalServiceId.Value);
            Assert.Equal(["ACTIVE", "CONFLICT", "INSTANCE_SELECTED", "TEMPLATE_SELECTED"],
                canonical.ManagerSource.Variables.Select(variable => variable.Name));
            Assert.Equal([1, 2, 3, 4, 5, 6], canonical.FileSources.Select(source => source.SourceId));
            Assert.Equal([false, true, true, false, false, false],
                canonical.FileSources.Select(source => source.IsOptional));
            Assert.Equal([false, true, false, false, false, false],
                canonical.FileSources.Select(source => source.IsMissing));
            Assert.Equal(canonical.FileSources.Select(source => source.IsMissing),
                alias.FileSources.Select(source => source.IsMissing));
            Assert.True(canonical.FileSources[0].Reveal().SequenceEqual(before[sourcePaths[0]]),
                "Source occurrence 1 did not match its fixed fixture.");
            Assert.True(canonical.FileSources[2].Reveal().SequenceEqual(before[sourcePaths[3]]),
                "Optional present source did not match its fixed fixture.");
            Assert.True(canonical.FileSources[3].Reveal().SequenceEqual(before[sourcePaths[1]]),
                "Source occurrence 4 did not match its fixed fixture.");
            Assert.True(canonical.FileSources[4].Reveal().SequenceEqual(before[sourcePaths[2]]),
                "Source occurrence 5 did not match its fixed fixture.");
            Assert.True(canonical.FileSources[5].Reveal().SequenceEqual(before[sourcePaths[0]]),
                "Repeated source occurrence did not preserve manager order.");
            foreach (var path in sourcePaths)
            {
                var after = File.ReadAllBytes(path);
                try
                {
                    Assert.True(before[path].AsSpan().SequenceEqual(after),
                        "A disposable source fixture changed during reading.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(after);
                }
            }
        }
        finally
        {
            foreach (var bytes in before.Values)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [Theory(Skip = "Requires real systemd and disposable composition fixtures.", SkipUnless = nameof(IsEnabled))]
    [InlineData("environment", false)]
    [InlineData("source", false)]
    [InlineData("source", true)]
    public async Task ComposesFixtureOwnedUniverseToPrivateManifest(string fixture, bool useAlias)
    {
        var prefix = RequiredPrefix();
        var isSource = string.Equals(fixture, "source", StringComparison.Ordinal);
        var canonicalName = prefix + (isSource ? "-source@sample.service" : "-environment@sample.service");
        var requestedName = useAlias ? prefix + "-source-alias@sample.service" : canonicalName;
        var manifestName = isSource ? "source.manifest" : "environment.manifest";
        var manifestPath = "/run/serval-environment-tests/" + prefix + "/" + manifestName;

        await using var observation = await ProductObservation.CreateAsync(
            prefix,
            canonicalName,
            isSource,
            TestContext.Current.CancellationToken);
        var reader = new SystemdServiceEnvironmentReader();
        using var composed = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await reader.ReadAsync(
                new SystemServiceId(requestedName),
                TestContext.Current.CancellationToken));
        Assert.Equal(canonicalName, composed.CanonicalServiceId.Value);
        AssertMatchesManifest(composed, manifestPath);
        await observation.AssertUnchangedAsync(TestContext.Current.CancellationToken);

        if (!isSource)
        {
            Assert.All(composed.Variables, variable => Assert.Equal(0, variable.WinningSourceId));
            Assert.Equal([0], composed.Sources.Select(source => source.Id));
            return;
        }

        Assert.Equal(0, WinningSource(composed, "ACTIVE"));
        Assert.Equal(0, WinningSource(composed, "TEMPLATE_SELECTED"));
        Assert.Equal(0, WinningSource(composed, "INSTANCE_SELECTED"));
        Assert.Equal(4, WinningSource(composed, "CONFLICT"));
        Assert.Equal(4, WinningSource(composed, "TWO"));
        Assert.Equal(5, WinningSource(composed, "EMPTY"));
        Assert.Equal(5, WinningSource(composed, "LATER"));
        Assert.Equal(5, WinningSource(composed, "THREE"));
        Assert.Equal(5, WinningSource(composed, "QUOTED"));
        Assert.Equal(5, WinningSource(composed, "CONTINUED"));
        Assert.Equal(5, WinningSource(composed, "MULTILINE"));
        Assert.Equal(3, WinningSource(composed, "OPTIONAL"));
        Assert.Equal(6, WinningSource(composed, "ONE"));
        Assert.Equal(6, WinningSource(composed, "REPEATED"));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], composed.Sources.Select(source => source.Id));
        Assert.True(composed.Sources[2].IsOptional && composed.Sources[2].IsMissing,
            "optional-missing-source");
        Assert.True(composed.Sources[3].IsOptional && !composed.Sources[3].IsMissing,
            "optional-present-source");
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task FailsClosedForRequiredMissingUnsupportedAndProtectedTargets()
    {
        var prefix = RequiredPrefix();
        var reader = new SystemdServiceEnvironmentReader();

        var missing = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source-required-missing.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.SourceUnavailable, missing.Code);
        Assert.Equal(1, missing.SourceId);

        var unsupported = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source-unsupported.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.UnsupportedConfiguration, unsupported.Code);
        Assert.Equal(EnvironmentUnsupportedReason.UnsetEnvironment, unsupported.Reason);

        var notFound = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source-nonexistent.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.NotFound, notFound.Code);

        var protectedResult = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("systemd-journald.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, protectedResult.Code);

        var instanceId = prefix["serval-enumeration-test-".Length..];
        var privilegedResult = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("serval-agent@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, privilegedResult.Code);

        var privilegedAliasResult = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("serval-exclusion-alias@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, privilegedAliasResult.Code);

        using var lookalike = Assert.IsType<ServiceEnvironmentReadResult.Success>(
            await reader.ReadAsync(
                new SystemServiceId("serval-agent-helper@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal("serval-agent-helper@" + instanceId + ".service", lookalike.CanonicalServiceId.Value);
        Assert.Single(lookalike.Sources);
        Assert.Empty(lookalike.Variables);
    }

    [Theory(Skip = "Requires real systemd and disposable failure fixtures.", SkipUnless = nameof(IsEnabled))]
    [InlineData("source-required-missing.service", EnvironmentReadFailureCode.SourceUnavailable, null, 1)]
    [InlineData("source-unsupported.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.UnsetEnvironment, null)]
    [InlineData("source-pass.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.PassEnvironment, null)]
    [InlineData("transient.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.TransientUnit, null)]
    [InlineData("source-generated.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.GeneratedUnit, null)]
    [InlineData("source-pattern.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.PathPattern, null)]
    [InlineData("source-specifier.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.UnresolvedSpecifier, null)]
    [InlineData("source-symlink.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.UnsafePath, 1)]
    [InlineData("source-special.service", EnvironmentReadFailureCode.UnsupportedConfiguration,
        EnvironmentUnsupportedReason.UnsafePath, 1)]
    public async Task RejectsRepresentableFailureWithoutPartialValues(
        string suffix,
        EnvironmentReadFailureCode expectedCode,
        EnvironmentUnsupportedReason? expectedReason,
        int? expectedSourceId)
    {
        var reader = new SystemdServiceEnvironmentReader();

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(RequiredPrefix() + "-" + suffix),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedReason, failure.Reason);
        Assert.Equal(expectedSourceId, failure.SourceId);
        AssertNegativeResultDoesNotLeakPrivateMarkers(failure, RequiredPrefix());
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task DetectsConfigurationPathDisappearanceDuringObservation()
    {
        var prefix = RequiredPrefix();
        var name = prefix + "-source-disappear.service";
        var path = "/run/systemd/system/" + name;
        var files = new DisappearingFixtureFileAccess(path);
        var reader = new SystemdEnvironmentSourceReader(
            async token => await SystemdDbusTransport.ConnectAsync(
                EnvironmentReadLimits.OperationDeadline,
                token).ConfigureAwait(false),
            files,
            TimeProvider.System);

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(name),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.True(files.Removed);
        Assert.False(File.Exists(path));
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task MapsManagerDisappearanceAfterRealInitialObservation()
    {
        var name = RequiredPrefix() + "-source@sample.service";
        DisappearingEnvironmentTransport? wrapper = null;
        var reader = new SystemdEnvironmentSourceReader(
            async token =>
            {
                var transport = await SystemdDbusTransport.ConnectAsync(
                    EnvironmentReadLimits.OperationDeadline,
                    token).ConfigureAwait(false);
                wrapper = new DisappearingEnvironmentTransport(transport);
                return wrapper;
            },
            new LinuxSystemdSourceFileAccess(),
            TimeProvider.System);

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(name),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Equal(2, wrapper?.EnvironmentReads);
    }

    [Theory(Skip = "Requires real systemd and disposable race fixtures.", SkipUnless = nameof(IsEnabled))]
    [InlineData(FixtureRace.SourceMutation)]
    [InlineData(FixtureRace.OptionalAppearance)]
    [InlineData(FixtureRace.OptionalDisappearance)]
    public async Task RejectsDeterministicFixtureRaceWithoutPartialPublication(FixtureRace race)
    {
        var prefix = RequiredPrefix();
        var path = race switch
        {
            FixtureRace.SourceMutation => "/run/systemd/system/" + prefix + "-source-sample-two.env",
            FixtureRace.OptionalAppearance => "/run/systemd/system/" + prefix + "-source-sample-optional-missing.env",
            _ => "/run/systemd/system/" + prefix + "-source-sample-optional-present.env",
        };
        using var files = new RacingFixtureFileAccess(path, race);
        var reader = new SystemdServiceEnvironmentReader(
            async (allowance, token) => await SystemdDbusTransport.ConnectAsync(allowance, token)
                .ConfigureAwait(false),
            files,
            TimeProvider.System);

        var failure = Assert.IsType<ServiceEnvironmentReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source@sample.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.True(files.Triggered, "fixture-race-not-triggered");
    }

    private static string RequiredPrefix()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        return prefix;
    }

    private static int WinningSource(ServiceEnvironmentReadResult.Success result, string name) =>
        Assert.Single(result.Variables, variable => variable.Name == name).WinningSourceId;

    private static void AssertMatchesManifest(ServiceEnvironmentReadResult.Success result, string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var manifest = ExpectationManifest.Read(stream);
        var presentCount = 0;
        var encoding = new UTF8Encoding(false, true);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(result);
        var privateRoot = Path.GetDirectoryName(path)!;
        var oracleOutput = File.ReadAllBytes(Path.Combine(privateRoot, "oracle-output.log"));
        var oracleJournal = File.ReadAllBytes(Path.Combine(privateRoot, "oracle-journal.log"));
        var parserMarker = File.ReadAllBytes(Path.Combine(privateRoot, "parser.marker"));
        var privatePath = Encoding.UTF8.GetBytes(path);
        try
        {
            Assert.False(ContainsSequence(serialized, parserMarker), "leakage-scan");
            Assert.False(ContainsSequence(oracleOutput, parserMarker), "leakage-scan");
            Assert.False(ContainsSequence(oracleJournal, parserMarker), "leakage-scan");
            Assert.False(ContainsSequence(serialized, privatePath), "leakage-scan");
            Assert.False(ContainsSequence(oracleOutput, privatePath), "leakage-scan");
            Assert.False(ContainsSequence(oracleJournal, privatePath), "leakage-scan");
            foreach (var entry in manifest.Entries)
            {
                var metadata = result.Variables.SingleOrDefault(variable => variable.Name == entry.Name);
                if (!entry.IsPresent)
                {
                    Assert.Null(metadata);
                    continue;
                }

                Assert.NotNull(metadata);
                presentCount++;
                var valueBytes = manifest.GetValueBytes(entry);
                Assert.False(ContainsSequence(serialized, valueBytes), "leakage-scan");
                Assert.False(ContainsSequence(oracleOutput, valueBytes), "leakage-scan");
                Assert.False(ContainsSequence(oracleJournal, valueBytes), "leakage-scan");
                var maximumChars = encoding.GetMaxCharCount(entry.ValueLength);
                var expected = ArrayPool<char>.Shared.Rent(Math.Max(maximumChars, 1));
                try
                {
                    var written = encoding.GetChars(valueBytes, expected);
                    Assert.True(
                        expected.AsSpan(0, written).SequenceEqual(result.Values.Reveal(entry.Name)),
                        "manifest-value");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(
                        System.Runtime.InteropServices.MemoryMarshal.AsBytes(expected.AsSpan(0, maximumChars)));
                    ArrayPool<char>.Shared.Return(expected);
                }
            }

            Assert.Equal(presentCount, result.Variables.Count);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialized);
            CryptographicOperations.ZeroMemory(oracleOutput);
            CryptographicOperations.ZeroMemory(oracleJournal);
            CryptographicOperations.ZeroMemory(parserMarker);
            CryptographicOperations.ZeroMemory(privatePath);
        }
    }

    private static bool ContainsSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        !needle.IsEmpty && haystack.IndexOf(needle) >= 0;

    private static void AssertNegativeResultDoesNotLeakPrivateMarkers(
        ServiceEnvironmentReadResult.Failure failure,
        string prefix)
    {
        var privateRoot = "/run/serval-environment-tests/" + prefix;
        var serialized = JsonSerializer.SerializeToUtf8Bytes(failure, failure.GetType());
        var oracleOutput = File.ReadAllBytes(Path.Combine(privateRoot, "oracle-output.log"));
        var oracleJournal = File.ReadAllBytes(Path.Combine(privateRoot, "oracle-journal.log"));
        var parserMarker = File.ReadAllBytes(Path.Combine(privateRoot, "parser.marker"));
        try
        {
            Assert.False(ContainsSequence(serialized, parserMarker), "leakage-scan");
            Assert.False(ContainsSequence(oracleOutput, parserMarker), "leakage-scan");
            Assert.False(ContainsSequence(oracleJournal, parserMarker), "leakage-scan");
            foreach (var manifestName in new[] { "environment.manifest", "source.manifest" })
            {
                var manifestPath = Path.Combine(privateRoot, manifestName);
                var privatePath = Encoding.UTF8.GetBytes(manifestPath);
                try
                {
                    Assert.False(ContainsSequence(serialized, privatePath), "leakage-scan");
                    Assert.False(ContainsSequence(oracleOutput, privatePath), "leakage-scan");
                    Assert.False(ContainsSequence(oracleJournal, privatePath), "leakage-scan");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(privatePath);
                }

                using var stream = new FileStream(
                    manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var manifest = ExpectationManifest.Read(stream);
                foreach (var entry in manifest.Entries)
                {
                    if (!entry.IsPresent)
                        continue;
                    var value = manifest.GetValueBytes(entry);
                    Assert.False(ContainsSequence(serialized, value), "leakage-scan");
                    Assert.False(ContainsSequence(oracleOutput, value), "leakage-scan");
                    Assert.False(ContainsSequence(oracleJournal, value), "leakage-scan");
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serialized);
            CryptographicOperations.ZeroMemory(oracleOutput);
            CryptographicOperations.ZeroMemory(oracleJournal);
            CryptographicOperations.ZeroMemory(parserMarker);
        }
    }

    private sealed class DisappearingFixtureFileAccess(string fixturePath) : ISystemdSourceFileAccess
    {
        private readonly LinuxSystemdSourceFileAccess _inner = new();
        public bool Removed { get; private set; }

        public async ValueTask<SystemdFileObservation> ObserveAsync(
            string path,
            bool readContent,
            bool allowMissing,
            CancellationToken cancellationToken,
            int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
        {
            var observation = await _inner.ObserveAsync(
                path, readContent, allowMissing, cancellationToken, maximumContentBytes)
                .ConfigureAwait(false);
            try
            {
                if (!Removed && string.Equals(path, fixturePath, StringComparison.Ordinal))
                {
                    File.Delete(fixturePath);
                    Removed = true;
                }
                return observation;
            }
            catch
            {
                observation.Dispose();
                throw;
            }
        }

        public ValueTask<bool> IsStableAsync(
            SystemdFileObservation observation,
            CancellationToken cancellationToken) =>
            _inner.IsStableAsync(observation, cancellationToken);
    }

    private sealed class ProductObservation : IAsyncDisposable
    {
        private readonly string _unitName;
        private readonly LinuxSystemdSourceFileAccess _files;
        private readonly List<(SystemdFileObservation Observation, ClearableBytes Content)> _snapshots;
        private readonly byte[] _lifecycleHash;
        private readonly byte[] _journalHash;
        private readonly IDisposable _reloadObserver;
        private readonly ReloadSignalState _reloadState;

        private ProductObservation(
            string unitName,
            LinuxSystemdSourceFileAccess files,
            List<(SystemdFileObservation, ClearableBytes)> snapshots,
            byte[] lifecycleHash,
            byte[] journalHash,
            IDisposable reloadObserver,
            ReloadSignalState reloadState)
        {
            _unitName = unitName;
            _files = files;
            _snapshots = snapshots;
            _lifecycleHash = lifecycleHash;
            _journalHash = journalHash;
            _reloadObserver = reloadObserver;
            _reloadState = reloadState;
        }

        internal static async Task<ProductObservation> CreateAsync(
            string prefix,
            string unitName,
            bool isSource,
            CancellationToken cancellationToken)
        {
            var paths = isSource
                ? new[]
                {
                    "/run/systemd/system/" + prefix + "-source@.service",
                    "/run/systemd/system/" + prefix + "-source@.service.d/10-manager.conf",
                    "/run/systemd/system/" + prefix + "-source@.service.d/20-sources.conf",
                    "/run/systemd/system/" + prefix + "-source@.service.d/30-repeat.conf",
                    "/run/systemd/system/" + prefix + "-source@sample.service.d/40-instance.conf",
                    "/run/systemd/system/" + prefix + "-source-sample-one.env",
                    "/run/systemd/system/" + prefix + "-source-sample-optional-present.env",
                    "/run/systemd/system/" + prefix + "-source-sample-two.env",
                    "/run/systemd/system/" + prefix + "-source-sample-three.env",
                    "/run/serval-environment-tests/" + prefix + "/source.manifest",
                }
                : new[]
                {
                    "/run/systemd/system/" + prefix + "-environment@sample.service",
                    "/run/systemd/system/" + prefix + "-environment@sample.service.d/10-environment.conf",
                    "/run/serval-environment-tests/" + prefix + "/environment.manifest",
                };
            var files = new LinuxSystemdSourceFileAccess();
            var snapshots = new List<(
                SystemdFileObservation Observation,
                ClearableBytes Content)>(paths.Length);
            IDisposable? reloadObserver = null;
            byte[]? lifecycleHash = null;
            byte[]? journalHash = null;
            try
            {
                foreach (var path in paths)
                {
                    var observed = await files.ObserveAsync(
                        path, readContent: true, allowMissing: false, cancellationToken)
                        .ConfigureAwait(false);
                    snapshots.Add((observed, observed.TakeContent()));
                }

                lifecycleHash = await SnapshotCommandAsync(
                    "/usr/bin/systemctl",
                    ["show", "--no-pager", "--property=ActiveState,SubState,InvocationID,StateChangeTimestampMonotonic,ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic", unitName],
                    cancellationToken).ConfigureAwait(false);
                journalHash = await SnapshotCommandAsync(
                    "/usr/bin/journalctl",
                    ["--quiet", "--unit=" + unitName, "--no-pager", "--output=short-monotonic"],
                    cancellationToken).ConfigureAwait(false);

                var reloadState = new ReloadSignalState();
                reloadObserver = await DBusConnection.System.AddMatchAsync(
                    new MatchRule
                    {
                        Type = MessageType.Signal,
                        Sender = "org.freedesktop.systemd1",
                        Path = "/org/freedesktop/systemd1",
                        Interface = "org.freedesktop.DBus.Properties",
                        Member = "PropertiesChanged",
                        Arg0 = "org.freedesktop.systemd1.Manager",
                    },
                    static (Message message, object? _) =>
                    {
                        var reader = message.GetBodyReader();
                        _ = reader.ReadString();
                        return reader.ReadDictionaryOfStringToVariantValue().ContainsKey("Reloading");
                    },
                    notification =>
                    {
                        if (notification.HasValue && notification.Value)
                            Interlocked.Exchange(ref reloadState.Observed, 1);
                    },
                    emitOnCapturedContext: false,
                    flags: ObserverFlags.None,
                    state: null).ConfigureAwait(false);

                return new ProductObservation(
                    unitName, files, snapshots, lifecycleHash, journalHash, reloadObserver, reloadState);
            }
            catch
            {
                reloadObserver?.Dispose();
                foreach (var snapshot in snapshots)
                {
                    snapshot.Content.Dispose();
                    snapshot.Observation.Dispose();
                }
                if (lifecycleHash is not null)
                    CryptographicOperations.ZeroMemory(lifecycleHash);
                if (journalHash is not null)
                    CryptographicOperations.ZeroMemory(journalHash);
                throw;
            }
        }

        internal async Task AssertUnchangedAsync(CancellationToken cancellationToken)
        {
            Assert.Equal(0, Volatile.Read(ref _reloadState.Observed));
            foreach (var snapshot in _snapshots)
            {
                Assert.True(
                    await _files.IsStableAsync(snapshot.Observation, cancellationToken)
                        .ConfigureAwait(false),
                    "product-observation-file-identity");
                var after = await File.ReadAllBytesAsync(
                    snapshot.Observation.Path, cancellationToken).ConfigureAwait(false);
                try
                {
                    Assert.True(
                        snapshot.Content.Reveal().SequenceEqual(after),
                        "product-observation-file-content");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(after);
                }
            }

            var lifecycleAfter = await SnapshotCommandAsync(
                "/usr/bin/systemctl",
                ["show", "--no-pager", "--property=ActiveState,SubState,InvocationID,StateChangeTimestampMonotonic,ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic", _unitName],
                cancellationToken).ConfigureAwait(false);
            var journalAfter = await SnapshotCommandAsync(
                "/usr/bin/journalctl",
                ["--quiet", "--unit=" + _unitName, "--no-pager", "--output=short-monotonic"],
                cancellationToken).ConfigureAwait(false);
            try
            {
                Assert.True(
                    CryptographicOperations.FixedTimeEquals(_lifecycleHash, lifecycleAfter),
                    "product-observation-lifecycle");
                Assert.True(
                    CryptographicOperations.FixedTimeEquals(_journalHash, journalAfter),
                    "product-observation-journal");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(lifecycleAfter);
                CryptographicOperations.ZeroMemory(journalAfter);
            }
        }

        public ValueTask DisposeAsync()
        {
            _reloadObserver.Dispose();
            foreach (var snapshot in _snapshots)
            {
                snapshot.Content.Dispose();
                snapshot.Observation.Dispose();
            }
            CryptographicOperations.ZeroMemory(_lifecycleHash);
            CryptographicOperations.ZeroMemory(_journalHash);
            return ValueTask.CompletedTask;
        }

        private static async Task<byte[]> SnapshotCommandAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)!;
            using var output = new MemoryStream();
            using var error = new MemoryStream();
            var outputCopy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var errorCopy = process.StandardError.BaseStream.CopyToAsync(error, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                await Task.WhenAll(outputCopy, errorCopy).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Product observation command failed.");
                return SHA256.HashData(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                CryptographicOperations.ZeroMemory(output.GetBuffer().AsSpan(0, checked((int)output.Length)));
                CryptographicOperations.ZeroMemory(error.GetBuffer().AsSpan(0, checked((int)error.Length)));
            }
        }

        private sealed class ReloadSignalState
        {
            internal int Observed;
        }
    }

    public enum FixtureRace
    {
        SourceMutation,
        OptionalAppearance,
        OptionalDisappearance,
    }

    private sealed class RacingFixtureFileAccess : ISystemdSourceFileAccess, IDisposable
    {
        private static readonly byte[] RaceBytes = "# race\n"u8.ToArray();
        private readonly LinuxSystemdSourceFileAccess _inner = new();
        private readonly string _path;
        private readonly FixtureRace _race;
        private readonly byte[]? _original;

        internal RacingFixtureFileAccess(string path, FixtureRace race)
        {
            _path = path;
            _race = race;
            if (race is not FixtureRace.OptionalAppearance)
                _original = File.ReadAllBytes(path);
        }

        internal bool Triggered { get; private set; }

        public async ValueTask<SystemdFileObservation> ObserveAsync(
            string path,
            bool readContent,
            bool allowMissing,
            CancellationToken cancellationToken,
            int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
        {
            var observation = await _inner.ObserveAsync(
                path, readContent, allowMissing, cancellationToken, maximumContentBytes)
                .ConfigureAwait(false);
            try
            {
                if (!Triggered && readContent && string.Equals(path, _path, StringComparison.Ordinal))
                {
                    if (_race == FixtureRace.SourceMutation)
                    {
                        using var destination = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                        destination.Write(RaceBytes);
                    }
                    else if (_race == FixtureRace.OptionalAppearance)
                    {
                        File.WriteAllBytes(path, RaceBytes);
                        SetOwnerOnlyMode(path);
                    }
                    else
                    {
                        File.Delete(path);
                    }

                    Triggered = true;
                }

                return observation;
            }
            catch
            {
                observation.Dispose();
                throw;
            }
        }

        public ValueTask<bool> IsStableAsync(
            SystemdFileObservation observation,
            CancellationToken cancellationToken) =>
            _inner.IsStableAsync(observation, cancellationToken);

        public void Dispose()
        {
            if (_race == FixtureRace.OptionalAppearance)
            {
                File.Delete(_path);
                return;
            }

            if (_original is null)
                return;
            try
            {
                File.WriteAllBytes(_path, _original);
                SetOwnerOnlyMode(_path);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(_original);
            }
        }

        private static void SetOwnerOnlyMode(string path)
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException();
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private sealed class DisappearingEnvironmentTransport(ISystemdDbusTransport inner)
        : ISystemdDbusTransport
    {
        public int EnvironmentReads { get; private set; }

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
            inner.GetManagerVersionAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(
            CancellationToken cancellationToken) => inner.ListUnitFilesAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(
            CancellationToken cancellationToken) => inner.ListServiceUnitsAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
            IReadOnlyCollection<SystemServiceId> serviceIds,
            CancellationToken cancellationToken) => inner.ListUnitsByNamesAsync(serviceIds, cancellationToken);

        public Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken) => inner.ReadUnitPropertiesAsync(unit, cancellationToken);

        public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken)
        {
            EnvironmentReads++;
            if (EnvironmentReads == 2)
                throw new SystemdDbusException(
                    SystemdDbusFailureKind.RemoteError,
                    "org.freedesktop.DBus.Error.UnknownObject");
            return inner.ReadEnvironmentPropertiesAsync(unit, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
