using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdServiceInspectorTests
{
    [Theory]
    [InlineData("systemd-journald.service", "ordinary.service", true)]
    [InlineData("ordinary.service", "ssh-backup.service", false)]
    public async Task ClassifiesCanonicalAndAliasNames(string canonical, string alias, bool expected)
    {
        var protocol = new InspectionProtocol
        {
            Properties = Properties(canonical, [canonical, alias]),
            Units = [Unit(canonical)],
        };
        var found = Assert.IsType<SystemdServiceInspectionResult.Found>(await Inspect(protocol, alias));
        Assert.Equal(expected, found.IsProtected);
    }

    [Theory]
    [InlineData("serval-agent.service", "ordinary.service", "ordinary.service")]
    [InlineData("ordinary.service", "serval-agent.service", "ordinary.service")]
    [InlineData("serval-agent@tenant.service", "helper@tenant.service", "helper@tenant.service")]
    public async Task AliasCannotReintroduceExcludedAgentUnit(
        string canonical, string alias, string requested)
    {
        var protocol = new InspectionProtocol
        {
            Properties = Properties(canonical, [canonical, alias]),
            Units = [Unit(canonical)],
        };

        var notFound = Assert.IsType<SystemdServiceInspectionResult.NotFound>(
            await Inspect(protocol, requested));
        Assert.Equal(requested, notFound.ServiceId.Value);
        Assert.Single(protocol.Lookups);
    }

    [Theory]
    [InlineData("serval-agent.service")]
    [InlineData("serval-agent@tenant.service")]
    public async Task DirectAgentNameIsExcludedBeforeConnecting(string name)
    {
        var connected = false;
        var inspector = new SystemdServiceInspector(_ =>
        {
            connected = true;
            throw new InvalidOperationException();
        }, TimeProvider.System);

        var result = await inspector.InspectAsync(
            new SystemServiceId(name), TestContext.Current.CancellationToken);

        Assert.Equal(name, Assert.IsType<SystemdServiceInspectionResult.NotFound>(result).ServiceId.Value);
        Assert.False(connected);
    }
    [Theory]
    [InlineData("canonical.service")]
    [InlineData("alias.service")]
    public async Task ResolvesCanonicalIdentityAndRetainsAllNames(string requested)
    {
        var protocol = new InspectionProtocol
        {
            Properties = Properties("canonical.service", ["canonical.service", "alias.service", "alias.service"]),
            Units = [Unit("canonical.service")],
        };
        var found = Assert.IsType<SystemdServiceInspectionResult.Found>(await Inspect(protocol, requested));
        Assert.Equal("canonical.service", found.Service.Id.Value);
        Assert.Equal("property description", found.Service.Description);
        Assert.Equal(["alias.service", "canonical.service"], found.Names.Select(name => name.Value));
        Assert.Equal([requested], Assert.Single(protocol.Lookups));
        Assert.Equal(1, protocol.Reads);
        Assert.True(protocol.Disposed);
    }

    [Theory]
    [InlineData("loaded", "active", "running")]
    [InlineData("loaded", "inactive", "dead")]
    [InlineData("loaded", "failed", "failed")]
    [InlineData("masked", "inactive", "dead")]
    [InlineData("future-load", "future-active", "future-sub")]
    public async Task ReturnsPropertyStatesWithoutUsingStaleListedStates(string load, string active, string sub)
    {
        var protocol = new InspectionProtocol { Properties = Properties() with { LoadState = load, ActiveState = active, SubState = sub } };
        var found = Assert.IsType<SystemdServiceInspectionResult.Found>(await Inspect(protocol));
        Assert.Equal(load, found.Service.LoadState.Value);
        Assert.Equal(active, found.Service.ActiveState.Value);
        Assert.Equal(sub, found.Service.SubState.Value);
    }

    [Fact]
    public async Task AcceptsConcreteInstance()
    {
        const string name = "worker@tenant.service";
        var protocol = new InspectionProtocol { Units = [Unit(name)], Properties = Properties(name) };
        var found = Assert.IsType<SystemdServiceInspectionResult.Found>(await Inspect(protocol, name));
        Assert.Equal(name, found.Service.Id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../bad.service")]
    [InlineData("bad\nSECRET.service")]
    [InlineData("x.timer")]
    [InlineData("x.service;id")]
    [InlineData("worker@.service")]
    public async Task RejectsInvalidOrTemplateInputBeforeConnecting(string name)
    {
        var connected = false;
        var inspector = new SystemdServiceInspector(_ =>
        {
            connected = true;
            throw new InvalidOperationException();
        }, TimeProvider.System);
        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await inspector.InspectAsync(new SystemServiceId(name), TestContext.Current.CancellationToken));
        Assert.False(connected);
    }

    [Fact]
    public async Task RejectsNullAndOverlongInputBeforeConnecting()
    {
        var inspector = new SystemdServiceInspector(_ => throw new InvalidOperationException(), TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentNullException>(() => inspector.InspectAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(async () => await inspector.InspectAsync(
            new SystemServiceId(new string('a', 256) + ".service"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("empty", false)]
    [InlineData("no-such-unit", false)]
    [InlineData("not-found", false)]
    [InlineData("unknown-object", false)]
    [InlineData("read-no-such-unit", false)]
    [InlineData("empty", true)]
    [InlineData("unknown-object", true)]
    public async Task RetriesAbsenceOnceAndReturnsExplicitResult(string absence, bool reappears)
    {
        var protocol = new InspectionProtocol();
        protocol.Lookup = (_, _) =>
        {
            if (reappears && protocol.Lookups.Count == 2) { return Task.FromResult(new[] { Unit() }); }
            if (absence == "no-such-unit") { throw Remote("org.freedesktop.systemd1.NoSuchUnit"); }
            return Task.FromResult(absence == "empty" ? Array.Empty<ProtocolListedUnit>() : [Unit()]);
        };
        protocol.Read = _ =>
        {
            if (reappears && protocol.Lookups.Count == 2) { return Task.FromResult(Properties()); }
            if (absence == "unknown-object") { throw Remote("org.freedesktop.DBus.Error.UnknownObject"); }
            if (absence == "read-no-such-unit") { throw Remote("org.freedesktop.systemd1.NoSuchUnit"); }
            return Task.FromResult(Properties() with { LoadState = "not-found" });
        };
        var result = await Inspect(protocol);
        Assert.Equal(2, protocol.Lookups.Count);
        if (reappears) { Assert.IsType<SystemdServiceInspectionResult.Found>(result); }
        else { Assert.Equal("a.service", Assert.IsType<SystemdServiceInspectionResult.NotFound>(result).ServiceId.Value); }
        Assert.True(protocol.Disposed);
    }

    [Theory]
    [InlineData("org.freedesktop.DBus.Error.AccessDenied", false)]
    [InlineData("org.freedesktop.DBus.Error.AccessDenied", true)]
    [InlineData("org.freedesktop.DBus.Error.UnknownObject", false)]
    [InlineData("org.freedesktop.DBus.Error.NoReply", true)]
    [InlineData("org.freedesktop.systemd1.UnitMasked", false)]
    [InlineData("UNTRUSTED PAYLOAD", true)]
    public async Task ManagerAndAccessFailuresStayTypedAndAreNotRetried(string remote, bool duringRead)
    {
        var protocol = new InspectionProtocol();
        if (duringRead) { protocol.Read = _ => throw Remote(remote); }
        else { protocol.Lookup = (_, _) => throw Remote(remote); }
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Inspect(protocol));
        Assert.Equal(SystemdDbusFailureKind.RemoteError, error.FailureKind);
        Assert.Single(protocol.Lookups);
        Assert.DoesNotContain("UNTRUSTED", error.ToString(), StringComparison.Ordinal);
        Assert.True(protocol.Disposed);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("alias")]
    [InlineData("missing-id")]
    [InlineData("missing-requested")]
    [InlineData("case-mismatch")]
    [InlineData("unrelated-listing")]
    [InlineData("multiple")]
    [InlineData("template-id")]
    [InlineData("template-alias")]
    [InlineData("template-listing")]
    [InlineData("null")]
    [InlineData("empty-state")]
    [InlineData("whitespace-load-state")]
    [InlineData("whitespace-active-state")]
    [InlineData("whitespace-sub-state")]
    [InlineData("long-state")]
    [InlineData("long-description")]
    public async Task RejectsMalformedOrUnrelatedReplies(string corruption)
    {
        var protocol = new InspectionProtocol();
        protocol.Properties = corruption switch
        {
            "id" => Properties() with { Id = "../SECRET.service" },
            "alias" => Properties() with { Names = ["a.service", "../SECRET.service"] },
            "missing-id" => Properties() with { Id = "b.service" },
            "missing-requested" => Properties("b.service"),
            "case-mismatch" => Properties("A.service"),
            "template-id" => Properties("worker@.service", ["worker@.service", "a.service"]),
            "template-alias" => Properties() with { Names = ["a.service", "worker@.service"] },
            "null" => null!,
            "empty-state" => Properties() with { ActiveState = "" },
            "whitespace-load-state" => Properties() with { LoadState = " " },
            "whitespace-active-state" => Properties() with { ActiveState = " " },
            "whitespace-sub-state" => Properties() with { SubState = " " },
            "long-state" => Properties() with { LoadState = new string('x', 129) },
            "long-description" => Properties() with { Description = new string('x', 16_385) },
            _ => Properties(),
        };
        protocol.Units = corruption switch
        {
            "unrelated-listing" => [Unit("b.service")],
            "template-listing" => [Unit("worker@.service")],
            "multiple" => [Unit(), Unit("b.service")],
            _ => [Unit()],
        };
        var error = await Assert.ThrowsAsync<SystemdDbusException>(() => Inspect(protocol));
        Assert.Equal(SystemdDbusFailureKind.MalformedReply, error.FailureKind);
        Assert.DoesNotContain("SECRET", error.ToString(), StringComparison.Ordinal);
        Assert.Single(protocol.Lookups);
    }

    [Theory]
    [InlineData("248", "UnsupportedVersion")]
    [InlineData("invalid", "IncompatibleReply")]
    public async Task RejectsUnsupportedManagerBeforeLookup(string version, string failure)
    {
        var protocol = new InspectionProtocol { Version = version };
        Assert.Equal(failure, (await Assert.ThrowsAsync<SystemdDbusException>(() => Inspect(protocol))).FailureKind.ToString());
        Assert.Empty(protocol.Lookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompatibleAndDisconnectedRepliesStayTyped(bool incompatible)
    {
        var protocol = new InspectionProtocol
        {
            Read = _ => throw new SystemdDbusProtocolException(incompatible ?
                SystemdDbusProtocolFailureKind.IncompatibleReply : SystemdDbusProtocolFailureKind.Unavailable),
        };
        Assert.Equal(incompatible ? SystemdDbusFailureKind.IncompatibleReply : SystemdDbusFailureKind.Unavailable,
            (await Assert.ThrowsAsync<SystemdDbusException>(() => Inspect(protocol))).FailureKind);
        Assert.Single(protocol.Lookups);
    }

    [Fact]
    public async Task PrecancelledCallerDoesNotConnect()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var inspector = new SystemdServiceInspector(_ => throw new InvalidOperationException(), TimeProvider.System);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            inspector.InspectAsync(new SystemServiceId("a.service"), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData("connect", false)]
    [InlineData("lookup", false)]
    [InlineData("read", false)]
    [InlineData("retry", false)]
    [InlineData("connect", true)]
    [InlineData("lookup", true)]
    [InlineData("read", true)]
    [InlineData("retry", true)]
    public async Task WholeOperationDeadlineAndCallerCancellationAbortOutstandingWork(string stage, bool callerCancels)
    {
        var time = new ManualTimeProvider();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        async Task Wait(CancellationToken token)
        {
            reached.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        var protocol = new InspectionProtocol();
        protocol.Lookup = async (_, token) =>
        {
            if (stage == "lookup" || (stage == "retry" && protocol.Lookups.Count == 2)) { await Wait(token); }
            return stage == "retry" ? [] : [Unit()];
        };
        protocol.Read = async token => { if (stage == "read") { await Wait(token); } return Properties(); };
        var inspector = new SystemdServiceInspector(async token =>
        {
            if (stage == "connect") { await Wait(token); }
            return new SystemdDbusTransport(protocol, TimeSpan.FromMinutes(1));
        }, time);
        var task = inspector.InspectAsync(new SystemServiceId("a.service"), cancellation.Token);
        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(5), time.DueTime);
        if (callerCancels)
        {
            cancellation.Cancel();
            time.Expire();
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task)).CancellationToken);
        }
        else
        {
            time.Expire();
            Assert.Equal(SystemdDbusFailureKind.Timeout, (await Assert.ThrowsAsync<SystemdDbusException>(() => task)).FailureKind);
        }
        if (stage != "connect") { Assert.True(protocol.Disposed); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterFinalReadCannotPublishSuccessOrNotFound(bool absent)
    {
        var time = new ManualTimeProvider();
        var protocol = new InspectionProtocol
        {
            Read = _ =>
        {
            time.Expire();
            return Task.FromResult(Properties() with { LoadState = absent ? "not-found" : "loaded" });
        }
        };
        var inspector = Create(protocol, time);
        Assert.Equal(SystemdDbusFailureKind.Timeout, (await Assert.ThrowsAsync<SystemdDbusException>(() =>
            inspector.InspectAsync(new SystemServiceId("a.service"), TestContext.Current.CancellationToken))).FailureKind);
    }

    private static Task<SystemdServiceInspectionResult> Inspect(InspectionProtocol protocol, string name = "a.service") =>
        Create(protocol, TimeProvider.System).InspectAsync(new SystemServiceId(name), TestContext.Current.CancellationToken);

    private static SystemdServiceInspector Create(InspectionProtocol protocol, TimeProvider time) =>
        new(_ => Task.FromResult<ISystemdDbusTransport>(new SystemdDbusTransport(protocol, TimeSpan.FromMinutes(1))), time);

    private static ProtocolListedUnit Unit(string name = "a.service") =>
        new(name, "stale description", "stale-load", "stale-active", "stale-sub", "", "/org/freedesktop/systemd1/unit/test");

    private static ProtocolUnitProperties Properties(string name = "a.service", string[]? names = null) =>
        new(name, names ?? [name], "property description", "loaded", "inactive", "dead");

    [Theory]
    [InlineData("alias@tenant.service", "worker@tenant.service", true)]
    [InlineData("alias@tenant.service", "worker@other.service", false)]
    [InlineData("alias.service", "worker@tenant.service", false)]
    [InlineData("alias@tenant.service", "worker.service", false)]
    [InlineData("alias@tenant.service", "worker@.service", false)]
    public async Task InspectionUsesTheSameInstanceAliasRules(string alias, string canonical, bool valid)
    {
        var protocol = new InspectionProtocol
        {
            Units = [Unit(canonical)],
            Properties = Properties(canonical, [alias, canonical]),
        };
        if (valid)
        {
            var found = Assert.IsType<SystemdServiceInspectionResult.Found>(await Inspect(protocol, alias));
            Assert.Equal(canonical, found.Service.Id.Value);
            Assert.Equal([alias, canonical], found.Names.Select(name => name.Value));
        }
        else
        {
            Assert.Equal(SystemdDbusFailureKind.MalformedReply,
                (await Assert.ThrowsAsync<SystemdDbusException>(() => Inspect(protocol, alias))).FailureKind);
        }
    }

    private static SystemdDbusProtocolException Remote(string name) => new(SystemdDbusProtocolFailureKind.RemoteError, name);

    private sealed class InspectionProtocol : ISystemdDbusProtocol
    {
        public string Version { get; init; } = "249";
        public ProtocolListedUnit[] Units { get; set; } = [Unit()];
        public ProtocolUnitProperties Properties { get; set; } = SystemdServiceInspectorTests.Properties();
        public List<string[]> Lookups { get; } = [];
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public Func<string[], CancellationToken, Task<ProtocolListedUnit[]>>? Lookup { get; set; }
        public Func<CancellationToken, Task<ProtocolUnitProperties>>? Read { get; set; }
        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) => Task.FromResult(Version);
        public Task<ProtocolUnitFileEntry[]> ListUnitFilesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Inspection must not enumerate files.");
        public Task<ProtocolListedUnit[]> ListUnitsByPatternsAsync(string[] states, string[] patterns, CancellationToken cancellationToken) => throw new InvalidOperationException("Inspection must not enumerate services.");
        public Task<ProtocolListedUnit[]> ListUnitsByNamesAsync(string[] names, CancellationToken cancellationToken)
        {
            Lookups.Add(names);
            return Lookup?.Invoke(names, cancellationToken) ?? Task.FromResult(Units);
        }
        public Task<ProtocolUnitProperties> ReadUnitPropertiesAsync(string objectPath, CancellationToken cancellationToken)
        {
            Assert.Equal("/org/freedesktop/systemd1/unit/test", objectPath);
            Reads++;
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(Properties);
        }
        public Task<ProtocolEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            string objectPath,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inspection must not read environment properties.");
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
