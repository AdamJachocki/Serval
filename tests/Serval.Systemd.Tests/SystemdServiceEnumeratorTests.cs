using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdServiceEnumeratorTests
{
    [Fact]
    public async Task ProtectionSurvivesMergedAliases()
    {
        var protocol = new EnumerationProtocol
        {
            Loaded = [Unit("ordinary.service", "first"), Unit("serval-agent.service", "second"),
                Unit("ssh-backup.service")],
        };
        protocol.Properties["first"] = Properties("ordinary.service");
        protocol.Properties["second"] = Properties("ordinary.service", ["ordinary.service", "serval-agent.service"]);
        var services = (await Enumerate(protocol, TestContext.Current.CancellationToken)).Services;
        Assert.True(Assert.Single(services, item => item.Service.Id.Value == "ordinary.service").IsProtected);
        Assert.False(Assert.Single(services, item => item.Service.Id.Value == "ssh-backup.service").IsProtected);
    }
    [Fact]
    public async Task CombinesInstalledAndLoadedServicesAndPreservesCanonicalNamesAndStates()
    {
        var protocol = new EnumerationProtocol
        {
            Files = [File("alias.service"), File("inactive.service"), File("worker@.service"),
                File("worker@installed.service"), File("ignored.timer")],
            Loaded = [Unit("z.service"), Unit("Alpha.service"), Unit("alpha.service"),
                Unit("worker@loaded.service"), Unit("transient.service"), Unit("generated.service")],
        };
        protocol.Properties["z.service"] = Properties("z.service", ["z.service", "alias.service"]);
        protocol.Properties["inactive.service"] = new("inactive.service", ["inactive.service"],
            "Inactive description", "future-load", "future-active", "future-sub");
        var snapshot = await Enumerate(protocol, TestContext.Current.CancellationToken);
        Assert.Equal(["Alpha.service", "alpha.service", "generated.service", "inactive.service",
            "transient.service", "worker@installed.service", "worker@loaded.service", "z.service"],
            snapshot.Services.Select(item => item.Service.Id.Value));
        Assert.Equal("worker@.service", Assert.Single(snapshot.Templates).Value);
        Assert.Equal(["inactive.service", "worker@installed.service"], Assert.Single(protocol.Lookups));
        var inactive = Assert.Single(snapshot.Services, item => item.Service.Id.Value == "inactive.service").Service;
        Assert.Equal("Inactive description", inactive.Description);
        Assert.Equal("future-load", inactive.LoadState.Value);
        Assert.Equal("future-active", inactive.ActiveState.Value);
        Assert.Equal("future-sub", inactive.SubState.Value);
        Assert.Equal(["alias.service", "z.service"], snapshot.Services[^1].Names.Select(name => name.Value));
        Assert.Empty(protocol.States);
        Assert.Equal(["*.service"], protocol.Patterns);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task AliasesShareOneObjectReadAndCanonicalIdsMergeAcrossObjects()
    {
        var protocol = new EnumerationProtocol
        {
            Loaded = [Unit("alias.service", "same"), Unit("canonical.service", "same"),
                Unit("other.service", "different")],
        };
        protocol.Properties["same"] = Properties("canonical.service", ["alias.service", "canonical.service"]);
        protocol.Properties["different"] = Properties("canonical.service", ["canonical.service", "other.service"]);
        var item = Assert.Single((await Enumerate(protocol, TestContext.Current.CancellationToken)).Services);
        Assert.Equal("canonical.service", item.Service.Id.Value);
        Assert.Equal(["alias.service", "canonical.service", "other.service"], item.Names.Select(name => name.Value));
        Assert.Equal(2, protocol.Reads);
    }

    [Theory]
    [InlineData("248", "UnsupportedVersion")]
    [InlineData("0", "UnsupportedVersion")]
    [InlineData("garbage", "IncompatibleReply")]
    [InlineData("999999999999999", "IncompatibleReply")]
    public async Task RejectsUnsupportedOrMalformedVersionBeforeEnumeration(string version, string failure)
    {
        var protocol = new EnumerationProtocol { Version = version };
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken));
        Assert.Equal(failure, error.FailureKind.ToString());
        Assert.False(protocol.FilesCalled);
    }

    [Theory]
    [InlineData("249")]
    [InlineData("255.4-1ubuntu8.17")]
    [InlineData("259 (259.1)")]
    public async Task AcceptsSupportedVersionAndEmptySnapshot(string version)
    {
        Assert.Empty((await Enumerate(new EnumerationProtocol { Version = version }, TestContext.Current.CancellationToken)).Services);
    }

    [Theory]
    [InlineData("bad name.service")]
    [InlineData("bad\nSECRET.service")]
    [InlineData("@instance.service")]
    public async Task RejectsMalformedInstalledNameBeforeNameLookup(string name)
    {
        var protocol = new EnumerationProtocol { Files = [File(name)] };
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken));
        Assert.Equal(SystemdDbusFailureKind.MalformedReply, error.FailureKind);
        Assert.Empty(protocol.Lookups);
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("listed")]
    [InlineData("id")]
    [InlineData("alias")]
    [InlineData("followed")]
    public async Task RejectsMalformedNamesFromEveryDbusSource(string source)
    {
        var unit = Unit("a.service");
        var protocol = new EnumerationProtocol
        {
            Loaded = [source == "listed" ? Unit("bad/name.service") : source == "followed"
                ? unit with { FollowedUnit = "bad/name.service" } : unit],
        };
        protocol.Properties["a.service"] = Properties(source == "id" ? "bad/name.service" : "a.service",
            source == "alias" ? ["a.service", "bad/name.service"] : ["a.service"]);
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInstalledServiceIsResolvedOnceMore(bool reappears)
    {
        var protocol = new EnumerationProtocol { Files = [File("missing.service")] };
        protocol.Lookup = names => protocol.Lookups.Count == 1 || !reappears ? [] : names.Select(name => Unit(name)).ToArray();
        var snapshot = await Enumerate(protocol, TestContext.Current.CancellationToken);
        Assert.Equal(2, protocol.Lookups.Count);
        Assert.Equal(reappears ? 1 : 0, snapshot.Services.Count);
    }

    [Theory]
    [InlineData("not-found", false)]
    [InlineData("unknown-object", false)]
    [InlineData("no-such-unit", false)]
    [InlineData("unknown-object", true)]
    public async Task DisappearingLoadedUnitIsRetriedWithinSameOperation(string absence, bool returns)
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit("a.service")] };
        protocol.Read = key =>
        {
            if (returns && protocol.Reads == 2)
            {
                return Properties(key);
            }

            if (absence == "not-found")
            {
                return new(key, [key], "", "not-found", "inactive", "dead");
            }

            throw new SystemdDbusProtocolException(SystemdDbusProtocolFailureKind.RemoteError,
                absence == "unknown-object" ? "org.freedesktop.DBus.Error.UnknownObject" : "org.freedesktop.systemd1.NoSuchUnit");
        };
        var snapshot = await Enumerate(protocol, TestContext.Current.CancellationToken);
        Assert.Single(protocol.Lookups);
        Assert.Equal(2, protocol.Reads);
        Assert.Equal(returns ? 1 : 0, snapshot.Services.Count);
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.AccessDenied")]
    [InlineData("org.freedesktop.DBus.Error.NoReply")]
    [InlineData("org.freedesktop.systemd1.UnitMasked")]
    public async Task FailureAfterSuccessfulRecordFailsSnapshotWithoutRetry(string remoteError)
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit("a.service"), Unit("z.service")] };
        protocol.Read = key => key == "a.service" ? Properties(key) :
            throw new SystemdDbusProtocolException(SystemdDbusProtocolFailureKind.RemoteError, remoteError);
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken));
        Assert.Equal(SystemdDbusFailureKind.RemoteError, error.FailureKind);
        Assert.Empty(protocol.Lookups);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task EarlierCallerCancellationAbortsAndDisposesTransport()
    {
        using var cancellation = new CancellationTokenSource();
        var protocol = new EnumerationProtocol();
        protocol.BeforeFiles = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Enumerate(protocol, cancellation.Token));
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task InternalDeadlineIncludesConnectionAndIsExactlyThirtySeconds()
    {
        var time = new ManualTimeProvider();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = new SystemdServiceEnumerator(async token =>
        {
            reached.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        }, time);
        var task = enumerator.EnumerateAsync(TestContext.Current.CancellationToken);
        await reached.Task;
        Assert.Equal(TimeSpan.FromSeconds(30), time.DueTime);
        time.Expire();
        Assert.Equal(SystemdDbusFailureKind.Timeout,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => task)).FailureKind);
    }

    [Fact]
    public async Task DeadlineAfterRecordsWereReadCannotPublishPartialSnapshot()
    {
        var time = new ManualTimeProvider();
        var protocol = new EnumerationProtocol { Loaded = [Unit("a.service")] };
        protocol.Read = key => { time.Expire(); return Properties(key); };
        var enumerator = Create(protocol, time);
        Assert.Equal(SystemdDbusFailureKind.Timeout,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => enumerator.EnumerateAsync(TestContext.Current.CancellationToken))).FailureKind);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task LoadedAndInstalledDisappearanceDoesNotReceiveAnExtraRetry()
    {
        var protocol = new EnumerationProtocol
        {
            Loaded = [Unit("a.service")],
            Files = [File("a.service")],
            Read = key => new(key, [key], "", "not-found", "inactive", "dead"),
        };
        Assert.Empty((await Enumerate(protocol, TestContext.Current.CancellationToken)).Services);
        Assert.Single(protocol.Lookups);
        Assert.Equal(2, protocol.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsInconsistentCanonicalIdentity(bool missingCanonical)
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit("listed.service")] };
        protocol.Properties["listed.service"] = Properties("canonical.service",
            missingCanonical ? ["listed.service"] : ["canonical.service"]);
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
    }

    [Fact]
    public async Task RejectsOverlongInstalledNameWithoutReuse()
    {
        var protocol = new EnumerationProtocol { Files = [File(new string('a', 256) + ".service")] };
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
        Assert.Empty(protocol.Lookups);
    }
    [Fact]
    public async Task MissingManagerObjectAfterLoadedRecordsFailsEntireSnapshot()
    {
        var protocol = new EnumerationProtocol
        {
            Loaded = [Unit("loaded.service")],
            Files = [File("installed.service")],
            Lookup = _ => throw new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.RemoteError, "org.freedesktop.DBus.Error.UnknownObject"),
        };
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() =>
            Enumerate(protocol, TestContext.Current.CancellationToken));
        Assert.Equal(SystemdDbusFailureKind.RemoteError, error.FailureKind);
        Assert.Equal("org.freedesktop.DBus.Error.UnknownObject", error.RemoteErrorName);
        Assert.Equal(1, protocol.Reads);
        Assert.Single(protocol.Lookups);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task DeduplicatesNamesWithinSingleObjectReply()
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit("a.service")] };
        protocol.Properties["a.service"] = Properties("a.service",
            ["a.service", "alias.service", "alias.service", "a.service"]);
        var item = Assert.Single((await Enumerate(protocol, TestContext.Current.CancellationToken)).Services);
        Assert.Equal(["a.service", "alias.service"], item.Names.Select(name => name.Value));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingAliasOwnershipAndCyclesFailRegardlessOfOrder(bool reverse)
    {
        foreach (var cyclic in new[] { false, true })
        {
            var units = new[] { Unit("a.service"), Unit("b.service") };
            var protocol = new EnumerationProtocol { Loaded = reverse ? units.Reverse().ToArray() : units };
            protocol.Properties["a.service"] = Properties("a.service", ["a.service", cyclic ? "b.service" : "shared.service"]);
            protocol.Properties["b.service"] = Properties("b.service", ["b.service", cyclic ? "a.service" : "shared.service"]);
            var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken));
            Assert.Equal(SystemdDbusFailureKind.MalformedReply, error.FailureKind);
            Assert.True(protocol.Disposed);
        }
    }

    [Fact]
    public async Task ThreeUnitAliasCycleFailsWithoutFollowingTheCycle()
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit("a.service"), Unit("b.service"), Unit("c.service")] };
        protocol.Properties["a.service"] = Properties("a.service", ["a.service", "c.service"]);
        protocol.Properties["b.service"] = Properties("b.service", ["b.service", "a.service"]);
        protocol.Properties["c.service"] = Properties("c.service", ["c.service", "b.service"]);
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
        Assert.Empty(protocol.Lookups);
    }

    [Fact]
    public async Task ReusedObjectCannotSilentlyDiscardAnUnrelatedInstance()
    {
        var protocol = new EnumerationProtocol
        {
            Loaded = [Unit("worker@a.service", "same"), Unit("worker@b.service", "same")],
        };
        protocol.Properties["same"] = Properties("worker@a.service");
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
        Assert.Equal(1, protocol.Reads);
    }

    [Theory]
    [InlineData("plain.service", "worker@a.service")]
    [InlineData("worker@a.service", "plain.service")]
    [InlineData("worker@a.service", "other@b.service")]
    [InlineData("worker@a.service", "worker@.service")]
    public async Task RejectsAliasesWithIncompatibleInstanceIdentity(string canonical, string alias)
    {
        var protocol = new EnumerationProtocol { Loaded = [Unit(canonical)] };
        protocol.Properties[canonical] = Properties(canonical, [canonical, alias]);
        Assert.Equal(SystemdDbusFailureKind.MalformedReply,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Enumerate(protocol, TestContext.Current.CancellationToken))).FailureKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstanceAliasesKeepFullCaseSensitiveEscapedIdentityAndDeterministicOrdering(bool reverse)
    {
        string[] instances = ["A", "a", @"tenant\x2d1", "tenant-1", "a@b"];
        var units = instances.Select(instance => Unit("worker@" + instance + ".service")).ToArray();
        var protocol = new EnumerationProtocol
        {
            Files = [File("worker@.service"), File("alias@.service")],
            Loaded = reverse ? units.Reverse().ToArray() : units,
        };
        foreach (var unit in units)
        {
            var alias = unit.Name.Replace("worker@", "alias@", StringComparison.Ordinal);
            protocol.Properties[unit.Name] = Properties(unit.Name, reverse ? [alias, unit.Name] : [unit.Name, alias, alias]);
        }
        var snapshot = await Enumerate(protocol, TestContext.Current.CancellationToken);
        Assert.Equal(units.Select(unit => unit.Name).Order(StringComparer.Ordinal), snapshot.Services.Select(item => item.Service.Id.Value));
        Assert.Equal(["alias@.service", "worker@.service"], snapshot.Templates.Select(name => name.Value));
        Assert.All(snapshot.Services, item => Assert.Equal(
            new[] { item.Service.Id.Value.Replace("worker@", "alias@", StringComparison.Ordinal), item.Service.Id.Value },
            item.Names.Select(name => name.Value)));
        Assert.Empty(protocol.Lookups);
    }

    private static Task<ServiceEnumerationSnapshot> Enumerate(EnumerationProtocol protocol, CancellationToken token) =>
        Create(protocol, TimeProvider.System).EnumerateAsync(token);

    private static SystemdServiceEnumerator Create(EnumerationProtocol protocol, TimeProvider time) =>
        new(_ => Task.FromResult<ISystemdDbusTransport>(new SystemdDbusTransport(protocol, TimeSpan.FromMinutes(1))), time);

    private static ProtocolUnitFileEntry File(string name) => new("/usr/lib/systemd/system/" + name, "disabled");

    private static ProtocolListedUnit Unit(string name, string? key = null) =>
        new(name, "listed description", "loaded", "inactive", "dead", "",
            "/org/freedesktop/systemd1/unit/" + (key ?? name).Replace(".", "_", StringComparison.Ordinal).Replace("@", "_", StringComparison.Ordinal).Replace("\\", "_", StringComparison.Ordinal).Replace("-", "_", StringComparison.Ordinal));

    private static ProtocolUnitProperties Properties(string name, string[]? names = null) =>
        new(name, names ?? [name], "description", "loaded", "inactive", "dead");

    private sealed class EnumerationProtocol : ISystemdDbusProtocol
    {
        public string Version { get; init; } = "255";
        public ProtocolUnitFileEntry[] Files { get; init; } = [];
        public ProtocolListedUnit[] Loaded { get; init; } = [];
        public Dictionary<string, ProtocolUnitProperties> Properties { get; } = new(StringComparer.Ordinal);
        public Func<string[], ProtocolListedUnit[]>? Lookup { get; set; }
        public Func<string, ProtocolUnitProperties>? Read { get; set; }
        public Action? BeforeFiles { get; set; }
        public List<string[]> Lookups { get; } = [];
        public string[] States { get; private set; } = [];
        public string[] Patterns { get; private set; } = [];
        public bool FilesCalled { get; private set; }
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) => Task.FromResult(Version);
        public Task<ProtocolUnitFileEntry[]> ListUnitFilesAsync(CancellationToken cancellationToken)
        {
            FilesCalled = true;
            BeforeFiles?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Files);
        }

        public Task<ProtocolListedUnit[]> ListUnitsByPatternsAsync(string[] states, string[] patterns, CancellationToken cancellationToken)
        {
            States = states;
            Patterns = patterns;
            Register(Loaded);
            return Task.FromResult(Loaded);
        }

        public Task<ProtocolListedUnit[]> ListUnitsByNamesAsync(string[] names, CancellationToken cancellationToken)
        {
            Lookups.Add(names);
            var units = Lookup?.Invoke(names) ?? names.Select(name => Unit(name)).ToArray();
            Register(units);
            return Task.FromResult(units);
        }

        private void Register(ProtocolListedUnit[] units)
        {
            foreach (var unit in units)
            {
                var key = Properties.Keys.FirstOrDefault(key => Unit(key).ObjectPath == unit.ObjectPath);
                _keys[unit.ObjectPath] = key ?? unit.Name;
            }
        }

        public Task<ProtocolUnitProperties> ReadUnitPropertiesAsync(string objectPath, CancellationToken cancellationToken)
        {
            Reads++;
            var key = _keys[objectPath];
            return Task.FromResult(Read?.Invoke(key) ?? Properties.GetValueOrDefault(key) ?? SystemdServiceEnumeratorTests.Properties(key));
        }

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public TimeSpan DueTime { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
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
