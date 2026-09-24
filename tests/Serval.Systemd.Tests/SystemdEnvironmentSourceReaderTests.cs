using Microsoft.Win32.SafeHandles;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdEnvironmentSourceReaderTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact]
    public async Task ReturnsCanonicalOrderedCompleteSnapshot()
    {
        var transport = new ReaderTransport
        {
            UnitProperties = UnitProperties("canonical.service", ["alias.service", "canonical.service"]),
            EnvironmentProperties = EnvironmentProperties(
                "canonical.service",
                ["alias.service", "canonical.service"],
                files:
                [
                    new SystemdEnvironmentFile("/etc/a.env", false),
                    new SystemdEnvironmentFile("/etc/a.env", false),
                    new SystemdEnvironmentFile("/etc/missing.env", true),
                ]),
        };
        var files = new ReaderFiles { Missing = ["/etc/missing.env"] };
        var reader = Create(transport, files);

        using var success = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await reader.ReadAsync(
                new SystemServiceId("alias.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal("canonical.service", success.CanonicalServiceId.Value);
        Assert.Equal([1, 2, 3], success.FileSources.Select(source => source.SourceId));
        Assert.Equal([false, false, true], success.FileSources.Select(source => source.IsOptional));
        Assert.Equal([false, false, true], success.FileSources.Select(source => source.IsMissing));
        Assert.Equal(2, success.FileSources[0].Reveal().Length);
        Assert.Equal(2, files.ObservedPaths.Count(path => path == "/etc/a.env"));
        Assert.Equal(2, transport.EnvironmentReads);
    }

    [Fact]
    public async Task RejectsEnvironmentSnapshotWhoseCanonicalIdChangedToAlias()
    {
        var names = new[] { "alias.service", "canonical.service" };
        var transport = new ReaderTransport
        {
            UnitProperties = UnitProperties("canonical.service", names),
            EnvironmentProperties = EnvironmentProperties("alias.service", names),
        };
        var files = new ReaderFiles();

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("alias.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Empty(files.ObservedPaths);
    }

    [Fact]
    public async Task AliasCountDoesNotConsumeTheEnvironmentSourceCountLimit()
    {
        var names = Enumerable.Range(0, EnvironmentReadLimits.MaxSources + 1)
            .Select(index => $"alias-{index}.service")
            .Prepend("canonical.service")
            .ToArray();
        var transport = new ReaderTransport
        {
            UnitProperties = UnitProperties("canonical.service", names),
            EnvironmentProperties = EnvironmentProperties("canonical.service", names),
        };

        using var success = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await Create(transport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("canonical.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal("canonical.service", success.CanonicalServiceId.Value);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task PassesRemainingAggregateByteBudgetToEachFileOccurrence(
        int excessBytes,
        bool expectedSuccess)
    {
        const int managerBytes = 9; // UTF-8 bytes in the fixed "A=manager" fixture entry.
        var paths = Enumerable.Range(0, 4).Select(index => $"/etc/source-{index}.env").ToArray();
        var transport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: paths
                .Select(path => new SystemdEnvironmentFile(path, false))
                .ToArray()),
        };
        var files = new ReaderFiles
        {
            ContentLengths = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [paths[0]] = EnvironmentReadLimits.MaxSourceBytes,
                [paths[1]] = EnvironmentReadLimits.MaxSourceBytes,
                [paths[2]] = EnvironmentReadLimits.MaxSourceBytes,
                [paths[3]] = EnvironmentReadLimits.MaxSourceBytes - managerBytes + excessBytes,
            },
        };

        var result = await Create(transport, files).ReadAsync(
            new SystemServiceId("a.service"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [EnvironmentReadLimits.MaxSourceBytes, EnvironmentReadLimits.MaxSourceBytes,
                EnvironmentReadLimits.MaxSourceBytes,
                EnvironmentReadLimits.MaxSourceBytes - managerBytes],
            files.ObservedContentBudgets);
        if (expectedSuccess)
        {
            using var success = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(result);
            Assert.Equal(4, success.FileSources.Count);
        }
        else
        {
            var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(result);
            Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, failure.Code);
            Assert.Equal(4, failure.SourceId);
        }
    }

    [Theory]
    [InlineData("dirty", EnvironmentReadFailureCode.InconsistentSnapshot, null)]
    [InlineData("transient", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.TransientUnit)]
    [InlineData("generated", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.GeneratedUnit)]
    [InlineData("unset", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.UnsetEnvironment)]
    [InlineData("pass", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.PassEnvironment)]
    [InlineData("pattern", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.PathPattern)]
    [InlineData("specifier", EnvironmentReadFailureCode.UnsupportedConfiguration, EnvironmentUnsupportedReason.UnresolvedSpecifier)]
    public async Task RejectsUnsupportedConfigurationBeforeFileAccess(
        string scenario,
        EnvironmentReadFailureCode code,
        EnvironmentUnsupportedReason? reason)
    {
        var properties = EnvironmentProperties(files: [new SystemdEnvironmentFile("/etc/a.env", false)]);
        properties = scenario switch
        {
            "dirty" => properties with { NeedDaemonReload = true },
            "transient" => properties with { Transient = true },
            "generated" => properties with { UnitFileState = "generated" },
            "unset" => properties with { UnsetEnvironment = ["A"] },
            "pass" => properties with { PassEnvironment = ["A"] },
            "pattern" => properties with { EnvironmentFiles = [new SystemdEnvironmentFile("/etc/*.env", false)] },
            _ => properties with { EnvironmentFiles = [new SystemdEnvironmentFile("/etc/%n.env", false)] },
        };
        var transport = new ReaderTransport { EnvironmentProperties = properties };
        var files = new ReaderFiles();

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(code, failure.Code);
        Assert.Equal(reason, failure.Reason);
        Assert.Empty(files.ObservedPaths);
    }

    [Fact]
    public async Task ProtectedAliasIsDeniedBeforeEnvironmentOrFileRead()
    {
        var transport = new ReaderTransport
        {
            UnitProperties = UnitProperties(
                "systemd-journald.service",
                ["ordinary-alias.service", "systemd-journald.service"]),
        };
        var files = new ReaderFiles();

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("ordinary-alias.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, failure.Code);
        Assert.Equal(0, transport.EnvironmentReads);
        Assert.Empty(files.ObservedPaths);
    }

    [Theory]
    [InlineData(false, EnvironmentReadFailureCode.SourceUnavailable)]
    [InlineData(true, null)]
    public async Task DistinguishesRequiredAndOptionalInitialAbsence(
        bool optional,
        EnvironmentReadFailureCode? expectedFailure)
    {
        const string path = "/etc/missing.env";
        var transport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: [new SystemdEnvironmentFile(path, optional)]),
        };
        var files = new ReaderFiles { Missing = [path] };

        var result = await Create(transport, files).ReadAsync(
            new SystemServiceId("a.service"),
            TestContext.Current.CancellationToken);
        if (expectedFailure is { } code)
        {
            var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(result);
            Assert.Equal(code, failure.Code);
            Assert.Equal(1, failure.SourceId);
        }
        else
        {
            using var success = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(result);
            Assert.True(Assert.Single(success.FileSources).IsMissing);
        }
    }

    [Fact]
    public async Task DetectsPropertyAndPathBindingChangesWithoutRetry()
    {
        var transport = new ReaderTransport { MutateFinalProperties = true };
        var propertyFailure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, propertyFailure.Code);
        Assert.Equal(2, transport.EnvironmentReads);

        var files = new ReaderFiles { Stable = false };
        var bindingFailure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(new ReaderTransport(), files).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, bindingFailure.Code);
    }

    [Fact]
    public async Task DisappearanceDuringFinalObservationIsInconsistent()
    {
        var transport = new ReaderTransport { DisappearOnFinalRead = true };
        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Equal(2, transport.EnvironmentReads);
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.UnknownProperty")]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface")]
    public async Task MissingRequiredPropertyDuringFinalObservationIsInconsistent(
        string remoteErrorName)
    {
        var transport = new ReaderTransport
        {
            FinalEnvironmentFailure = new SystemdDbusException(
                SystemdDbusFailureKind.RemoteError,
                remoteErrorName),
        };

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Equal(2, transport.EnvironmentReads);
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.UnknownProperty")]
    [InlineData("org.freedesktop.DBus.Error.UnknownInterface")]
    public async Task MissingRequiredInitialPropertyIsUnsupported(string remoteErrorName)
    {
        var transport = new ReaderTransport
        {
            InitialEnvironmentFailure = new SystemdDbusException(
                SystemdDbusFailureKind.RemoteError,
                remoteErrorName),
        };
        var files = new ReaderFiles();

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.UnsupportedConfiguration, failure.Code);
        Assert.Equal(EnvironmentUnsupportedReason.UnsupportedProperty, failure.Reason);
        Assert.Empty(files.ObservedPaths);
    }

    [Theory]
    [InlineData("org.freedesktop.systemd1.NoSuchUnit")]
    [InlineData("org.freedesktop.DBus.Error.UnknownObject")]
    public async Task DisappearanceDuringInitialObservationIsInconsistent(string remoteErrorName)
    {
        var transport = new ReaderTransport
        {
            InitialEnvironmentFailure = new SystemdDbusException(
                SystemdDbusFailureKind.RemoteError,
                remoteErrorName),
        };
        var files = new ReaderFiles();

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Empty(files.ObservedPaths);
    }

    [Fact]
    public async Task DbusDecodeLimitExceededIsPreservedAtTheReaderBoundary()
    {
        var transport = new ReaderTransport
        {
            InitialEnvironmentFailure = new SystemdDbusException(
                SystemdDbusFailureKind.LimitExceeded),
        };

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, failure.Code);
    }

    [Fact]
    public async Task PartialCandidateIsClearedAndFailureMetadataDoesNotExposePath()
    {
        var transport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files:
            [
                new SystemdEnvironmentFile("/etc/first.env", false),
                new SystemdEnvironmentFile("/etc/second.env", false),
            ]),
        };
        var files = new ReaderFiles { FailurePath = "/etc/second.env" };
        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(transport, files).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.SourceUnavailable, failure.Code);
        Assert.Equal(2, failure.SourceId);
        Assert.NotEmpty(files.ClearedBuffers);
        Assert.All(files.ClearedBuffers,
            buffer => Assert.All(buffer, value => Assert.Equal(0, value)));
        Assert.DoesNotContain("/etc", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationRemainsAssociatedWithCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(new ReaderTransport(), new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task PrecancelledProtectedRequestPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connected = false;
        var reader = new SystemdEnvironmentSourceReader(
            _ => { connected = true; throw new InvalidOperationException(); },
            new ReaderFiles(),
            TimeProvider.System);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(new SystemServiceId("serval-agent.service"), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(connected);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CancellationPrecedesOrdinaryFileAndStabilityFailures(
        bool cancelCaller,
        bool failDuringFileRead)
    {
        const string filePath = "/etc/late-failure.env";
        var time = new ManualTimeProvider();
        using var callerCancellation = new CancellationTokenSource();
        Action signal = cancelCaller ? callerCancellation.Cancel : time.Expire;
        var files = new ReaderFiles
        {
            FailurePath = failDuringFileRead ? filePath : null,
            Stable = failDuringFileRead,
            OnObserve = failDuringFileRead ? path =>
            {
                if (path == filePath)
                    signal();
            }
            : null,
            OnIsStable = failDuringFileRead ? null : signal,
        };
        var transport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files:
                [new SystemdEnvironmentFile(filePath, false)]),
        };
        var reader = new SystemdEnvironmentSourceReader(
            _ => Task.FromResult<ISystemdDbusTransport>(transport),
            files,
            time);

        if (cancelCaller)
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                reader.ReadAsync(new SystemServiceId("a.service"), callerCancellation.Token));
            Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        }
        else
        {
            var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
                await reader.ReadAsync(new SystemServiceId("a.service"), callerCancellation.Token));
            Assert.Equal(EnvironmentReadFailureCode.Timeout, failure.Code);
        }
    }

    [Fact]
    public async Task EnforcesSourceCountAndRepeatedOccurrenceByteAccounting()
    {
        var exactDeclarations = Enumerable.Range(0, EnvironmentReadLimits.MaxSources - 1)
            .Select(_ => new SystemdEnvironmentFile("/etc/repeated.env", false)).ToArray();
        var exactTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: exactDeclarations) with { Environment = [] },
        };
        using var exact = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await Create(exactTransport, new ReaderFiles { ContentLength = 0 }).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadLimits.MaxSources - 1, exact.FileSources.Count);

        var excessTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: [.. exactDeclarations,
                new SystemdEnvironmentFile("/etc/excess.env", false)]),
        };
        var sourceLimit = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(excessTransport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, sourceLimit.Code);

        var repeated = Enumerable.Repeat(
            new SystemdEnvironmentFile("/etc/repeated.env", false), 4).ToArray();
        var exactBytesTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: repeated) with { Environment = [] },
        };
        using var exactBytes = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await Create(exactBytesTransport, new ReaderFiles
            {
                ContentLength = EnvironmentReadLimits.MaxSourceBytes,
            }).ReadAsync(new SystemServiceId("a.service"), TestContext.Current.CancellationToken));
        Assert.Equal(4, exactBytes.FileSources.Count);

        var excessBytesTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: [.. repeated,
                new SystemdEnvironmentFile("/etc/repeated.env", false)]) with
            { Environment = [] },
        };
        var totalLimit = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(excessBytesTransport, new ReaderFiles
            {
                ContentLength = EnvironmentReadLimits.MaxSourceBytes,
            }).ReadAsync(new SystemServiceId("a.service"), TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, totalLimit.Code);
        Assert.Equal(5, totalLimit.SourceId);
    }

    [Fact]
    public async Task EnforcesUtf8PathAndConfigurationPathBoundaries()
    {
        var exactPath = "/" + new string('é', (EnvironmentReadLimits.MaxPathBytes - 2) / 2) + "x";
        var exactPathTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files: [new SystemdEnvironmentFile(exactPath, false)]),
        };
        using var exactPathResult = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await Create(exactPathTransport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Single(exactPathResult.FileSources);

        var longPathTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties(files:
                [new SystemdEnvironmentFile(exactPath + "é", false)]),
        };
        var pathLimit = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(longPathTransport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, pathLimit.Code);

        var exactDropIns = Enumerable.Range(0, EnvironmentReadLimits.MaxConfigurationPaths - 1)
            .Select(index => $"/etc/systemd/system/a.service.d/{index:D3}.conf").ToArray();
        var exactConfigTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties() with { DropInPaths = exactDropIns },
        };
        using var exactConfig = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await Create(exactConfigTransport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));

        var excessConfigTransport = new ReaderTransport
        {
            EnvironmentProperties = EnvironmentProperties() with
            {
                DropInPaths = [.. exactDropIns, "/etc/systemd/system/a.service.d/excess.conf"],
            },
        };
        var configLimit = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await Create(excessConfigTransport, new ReaderFiles()).ReadAsync(
                new SystemServiceId("a.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded, configLimit.Code);
    }

    [Fact]
    public async Task WholeOperationDeadlineMapsToTimeout()
    {
        var time = new ManualTimeProvider();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new SystemdEnvironmentSourceReader(
            async token =>
            {
                reached.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null!;
            },
            new ReaderFiles(),
            time);
        var read = reader.ReadAsync(new SystemServiceId("a.service"), CancellationToken.None);
        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(EnvironmentReadLimits.OperationDeadline, time.DueTime);
        time.Expire();
        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(await read);
        Assert.Equal(EnvironmentReadFailureCode.Timeout, failure.Code);
    }

    [Theory(Skip = "Requires Linux descriptor and statx semantics.", SkipUnless = nameof(IsLinux))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSynchronousOpenIsDisposedAndCallerCancellationWins(bool cancelCaller)
    {
        var backingPath = Path.GetTempFileName();
        try
        {
            var time = new ManualTimeProvider();
            using var callerCancellation = new CancellationTokenSource();
            var handles = new List<SafeFileHandle>();
            var openCalls = 0;
            var files = new LinuxSystemdSourceFileAccess(
                static (handle, buffer, offset, token) =>
                    RandomAccess.ReadAsync(handle, buffer, offset, token),
                (_, _, _) =>
                {
                    var handle = File.OpenHandle(
                        backingPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        FileOptions.RandomAccess);
                    handles.Add(handle);
                    openCalls++;
                    if (openCalls == 2)
                    {
                        if (cancelCaller)
                            callerCancellation.Cancel();
                        time.Expire();
                    }
                    return handle;
                });
            var transport = new ReaderTransport
            {
                EnvironmentProperties = EnvironmentProperties(files:
                    [new SystemdEnvironmentFile("/etc/late.env", false)]),
            };
            var reader = new SystemdEnvironmentSourceReader(
                _ => Task.FromResult<ISystemdDbusTransport>(transport),
                files,
                time);

            if (cancelCaller)
            {
                var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    reader.ReadAsync(new SystemServiceId("a.service"), callerCancellation.Token));
                Assert.Equal(callerCancellation.Token, exception.CancellationToken);
            }
            else
            {
                var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
                    await reader.ReadAsync(
                        new SystemServiceId("a.service"),
                        callerCancellation.Token));
                Assert.Equal(EnvironmentReadFailureCode.Timeout, failure.Code);
            }

            Assert.Equal(2, openCalls);
            Assert.All(handles, handle => Assert.True(handle.IsClosed));
        }
        finally
        {
            File.Delete(backingPath);
        }
    }

    [Fact]
    public async Task InvalidInputAndDirectProtectedUnitDoNotConnect()
    {
        var connected = false;
        var reader = new SystemdEnvironmentSourceReader(
            _ => { connected = true; throw new InvalidOperationException(); },
            new ReaderFiles(),
            TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(
            new SystemServiceId("worker@.service"),
            TestContext.Current.CancellationToken));
        var protectedResult = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("serval-agent.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, protectedResult.Code);
        Assert.False(connected);
    }

    private static SystemdEnvironmentSourceReader Create(ReaderTransport transport, ReaderFiles files) =>
        new(_ => Task.FromResult<ISystemdDbusTransport>(transport), files, TimeProvider.System);

    private static SystemdUnitProperties UnitProperties(string id = "a.service", string[]? names = null) =>
        new(id, names ?? [id], "description", "loaded", "inactive", "dead");

    private static SystemdEnvironmentProperties EnvironmentProperties(
        string id = "a.service",
        string[]? names = null,
        SystemdEnvironmentFile[]? files = null) =>
        new(
            id,
            names ?? [id],
            "loaded",
            "/etc/systemd/system/a.service",
            [],
            false,
            false,
            "enabled",
            ["A=manager"],
            files ?? [],
            [],
            []);

    private sealed class ReaderFiles : ISystemdSourceFileAccess
    {
        public HashSet<string> Missing { get; init; } = [];
        public bool Stable { get; init; } = true;
        public int ContentLength { get; init; } = 2;
        public Dictionary<string, int>? ContentLengths { get; init; }
        public string? FailurePath { get; init; }
        public Action<string>? OnObserve { get; init; }
        public Action? OnIsStable { get; init; }
        public List<string> ObservedPaths { get; } = [];
        public List<int> ObservedContentBudgets { get; } = [];
        public List<byte[]> ClearedBuffers { get; } = [];

        public ValueTask<SystemdFileObservation> ObserveAsync(
            string path,
            bool readContent,
            bool allowMissing,
            CancellationToken cancellationToken,
            int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObservedPaths.Add(path);
            OnObserve?.Invoke(path);
            if (string.Equals(path, FailurePath, StringComparison.Ordinal))
                throw new SystemdSourceFileException(SystemdSourceFileFailure.Unavailable);
            if (Missing.Contains(path))
            {
                if (allowMissing)
                    return ValueTask.FromResult(new SystemdFileObservation(path, missing: true));
                throw new SystemdSourceFileException(SystemdSourceFileFailure.Missing);
            }

            if (!readContent)
                return ValueTask.FromResult(new SystemdFileObservation(path, new ClearableBytes()));
            ObservedContentBudgets.Add(maximumContentBytes);
            var contentLength = ContentLengths is not null && ContentLengths.TryGetValue(path, out var length)
                ? length
                : ContentLength;
            if (contentLength > maximumContentBytes)
                throw new SystemdSourceFileException(SystemdSourceFileFailure.LimitExceeded);
            var content = new ClearableBytes(clearedBufferObserver: ClearedBuffers.Add);
            content.Append(new byte[contentLength]);
            return ValueTask.FromResult(new SystemdFileObservation(path, content));
        }

        public ValueTask<bool> IsStableAsync(
            SystemdFileObservation observation,
            CancellationToken cancellationToken)
        {
            OnIsStable?.Invoke();
            return ValueTask.FromResult(Stable);
        }
    }

    private sealed class ReaderTransport : ISystemdDbusTransport
    {
        private readonly SystemdUnitReference _unit = new();
        public SystemdUnitProperties UnitProperties { get; init; } = SystemdEnvironmentSourceReaderTests.UnitProperties();
        public SystemdEnvironmentProperties EnvironmentProperties { get; init; } = SystemdEnvironmentSourceReaderTests.EnvironmentProperties();
        public bool MutateFinalProperties { get; init; }
        public bool DisappearOnFinalRead { get; init; }
        public Exception? InitialEnvironmentFailure { get; init; }
        public Exception? FinalEnvironmentFailure { get; init; }
        public int EnvironmentReads { get; private set; }

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) => Task.FromResult("255");
        public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
            IReadOnlyCollection<SystemServiceId> serviceIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SystemdListedUnit>>(
                [new SystemdListedUnit(UnitProperties.Id, "description", "loaded", "inactive", "dead", "", _unit)]);
        public Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken) => Task.FromResult(UnitProperties);
        public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken)
        {
            EnvironmentReads++;
            if (EnvironmentReads == 1 && InitialEnvironmentFailure is not null)
                throw InitialEnvironmentFailure;
            if (EnvironmentReads == 2 && FinalEnvironmentFailure is not null)
                throw FinalEnvironmentFailure;
            if (DisappearOnFinalRead && EnvironmentReads == 2)
                throw new SystemdDbusException(
                    SystemdDbusFailureKind.RemoteError,
                    "org.freedesktop.DBus.Error.UnknownObject");
            return Task.FromResult(MutateFinalProperties && EnvironmentReads == 2
                ? EnvironmentProperties with { UnitFileState = "disabled" }
                : EnvironmentProperties);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public TimeSpan DueTime { get; private set; }
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _callback = callback;
            _state = state;
            DueTime = dueTime;
            return new ManualTimer();
        }
        public void Expire() => _callback!(_state);
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
