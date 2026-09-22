using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Tmds.DBus.Protocol;
using Xunit;

namespace Serval.Systemd.Tests.DBus;

public sealed class SystemdDbusTransportTests
{
    private static readonly ProtocolListedUnit ListedUnit = new(
        "alpha.service",
        "Alpha",
        "loaded",
        "active",
        "running",
        string.Empty,
        "/org/freedesktop/systemd1/unit/alpha_2eservice");

    [Fact]
    public async Task TypedCallsReturnValidatedRepliesAndUseFixedDiscoveryPattern()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersion = "systemd 255",
            UnitFiles =
            [
                new ProtocolUnitFileEntry("/usr/lib/systemd/system/alpha.service", "enabled"),
            ],
            ListedUnits = [ListedUnit],
            UnitProperties = new ProtocolUnitProperties(
                "alpha.service",
                ["alpha.service", "alpha-alias.service"],
                "Alpha",
                "loaded",
                "active",
                "running"),
            EnvironmentProperties = new ProtocolEnvironmentProperties(
                "alpha.service",
                ["alpha.service", "alpha-alias.service"],
                "loaded",
                "/etc/systemd/system/alpha.service",
                ["/etc/systemd/system/alpha.service.d/10-env.conf"],
                false,
                false,
                "enabled",
                ["A=value"],
                [new ProtocolEnvironmentFile("/etc/alpha.env", true)],
                [],
                []),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var version = await transport.GetManagerVersionAsync(TestContext.Current.CancellationToken);
        var unitFiles = await transport.ListUnitFilesAsync(TestContext.Current.CancellationToken);
        var listedUnits = await transport.ListServiceUnitsAsync(
            TestContext.Current.CancellationToken);
        var namedUnits = await transport.ListUnitsByNamesAsync(
            [new SystemServiceId("alpha.service")],
            TestContext.Current.CancellationToken);
        var properties = await transport.ReadUnitPropertiesAsync(
            listedUnits[0].Unit,
            TestContext.Current.CancellationToken);
        var environment = await transport.ReadEnvironmentPropertiesAsync(
            listedUnits[0].Unit,
            TestContext.Current.CancellationToken);

        Assert.Equal("systemd 255", version);
        var unitFile = Assert.Single(unitFiles);
        Assert.Equal("/usr/lib/systemd/system/alpha.service", unitFile.Path);
        Assert.Equal("enabled", unitFile.State);
        Assert.Equal("alpha.service", Assert.Single(listedUnits).Name);
        Assert.Equal("alpha.service", Assert.Single(namedUnits).Name);
        Assert.Equal("alpha.service", properties.Id);
        Assert.Equal(["alpha.service", "alpha-alias.service"], properties.Names);
        Assert.Equal("alpha.service", environment.Id);
        Assert.Equal(["A=value"], environment.Environment);
        Assert.True(Assert.Single(environment.EnvironmentFiles).IgnoreErrors);
        Assert.Empty(protocol.ObservedStates);
        Assert.Equal(["*.service"], protocol.ObservedPatterns);
        Assert.Equal(["alpha.service"], protocol.ObservedNames);
        Assert.Equal(ListedUnit.ObjectPath, protocol.ObservedObjectPath);
    }

    [Fact]
    public async Task EnvironmentPropertiesAcceptAliasCountBeyondSourceCount()
    {
        var names = Enumerable.Range(0, EnvironmentReadLimits.MaxSources + 1)
            .Select(index => $"alias-{index}.service")
            .Prepend("alpha.service")
            .ToArray();
        var protocol = new FakeSystemdDbusProtocol
        {
            ListedUnits = [ListedUnit],
            EnvironmentProperties = new ProtocolEnvironmentProperties(
                "alpha.service", names, "loaded", "/etc/alpha.service", [], false,
                false, "enabled", [], [], [], []),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));
        var unit = Assert.Single(await transport.ListServiceUnitsAsync(TestContext.Current.CancellationToken));

        var properties = await transport.ReadEnvironmentPropertiesAsync(
            unit.Unit,
            TestContext.Current.CancellationToken);

        Assert.Equal(names.Length, properties.Names.Count);
    }

    [Fact]
    public async Task RemoteErrorIsMappedWithoutUntrustedDetails()
    {
        const string untrustedErrorName = "org.freedesktop.DBus.Error.AccessDenied\nSECRET";
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersionFailure = new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.RemoteError,
                untrustedErrorName),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.RemoteError, exception.FailureKind);
        Assert.Null(exception.RemoteErrorName);
        Assert.DoesNotContain("SECRET", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(untrustedErrorName, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidRemoteErrorNameIsReturnedAsBoundedMetadata()
    {
        const string errorName = "org.freedesktop.DBus.Error.AccessDenied";
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersionFailure = new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.RemoteError,
                errorName),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(errorName, exception.RemoteErrorName);
        Assert.DoesNotContain(errorName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedDbusMessageIsMappedWithoutRawDiagnostics()
    {
        const string rawMessage = "malformed reply SECRET";
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersionFailure = TmdsSystemdDbusProtocol.MapReadException(
                new DBusReadException(rawMessage)),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.IncompatibleReply, exception.FailureKind);
        Assert.DoesNotContain(rawMessage, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncompatibleSignatureIsMappedToTypedFailure()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersionFailure = new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.IncompatibleReply),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.IncompatibleReply, exception.FailureKind);
    }

    [Fact]
    public async Task ProtocolReplyLimitIsPreservedAsTypedFailure()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            ManagerVersionFailure = new SystemdDbusProtocolException(
                SystemdDbusProtocolFailureKind.LimitExceeded),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.LimitExceeded, exception.FailureKind);
    }

    [Theory]
    [InlineData("unit-name")]
    [InlineData("object-root")]
    [InlineData("object-path")]
    [InlineData("followed-unit")]
    [InlineData("description")]
    public async Task MalformedListedUnitReplyIsMappedToTypedFailure(string field)
    {
        var malformedUnit = field switch
        {
            "unit-name" => ListedUnit with { Name = "not-a-service.socket" },
            "object-root" => ListedUnit with { ObjectPath = "/org/freedesktop/systemd1/job/42" },
            "object-path" => ListedUnit with { ObjectPath = "/org/freedesktop/systemd1/unit/invalid/path" },
            "followed-unit" => ListedUnit with { FollowedUnit = "not-a-service.socket" },
            _ => ListedUnit with { Description = new string('x', 16_385) },
        };
        var protocol = new FakeSystemdDbusProtocol
        {
            ListedUnits = [malformedUnit],
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.ListServiceUnitsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.MalformedReply, exception.FailureKind);
    }

    [Fact]
    public async Task MissingRequiredUnitPropertyIsMappedToMalformedReply()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            ListedUnits = [ListedUnit],
            UnitProperties = new ProtocolUnitProperties(
                "alpha.service",
                [],
                "Alpha",
                "loaded",
                "active",
                "running"),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));
        var unit = Assert.Single(
            await transport.ListServiceUnitsAsync(TestContext.Current.CancellationToken));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.ReadUnitPropertiesAsync(
                unit.Unit,
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.MalformedReply, exception.FailureKind);
    }

    [Fact]
    public async Task OversizedEnvironmentReplyIsRejectedWithoutPayloadDiagnostics()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            ListedUnits = [ListedUnit],
            EnvironmentProperties = new ProtocolEnvironmentProperties(
                "alpha.service", ["alpha.service"], "loaded", "/etc/a.service", [], false,
                false, "enabled", Enumerable.Repeat("A=secret", EnvironmentReadLimits.MaxAssignments + 2).ToArray(),
                [], [], []),
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));
        var unit = Assert.Single(await transport.ListServiceUnitsAsync(TestContext.Current.CancellationToken));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(() =>
            transport.ReadEnvironmentPropertiesAsync(unit.Unit, TestContext.Current.CancellationToken));

        Assert.Equal(SystemdDbusFailureKind.MalformedReply, exception.FailureKind);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeadlineExpiryIsDistinctAndAbortsOutstandingCall()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            WaitForManagerVersionCancellation = true,
        };
        await using var transport = new SystemdDbusTransport(
            protocol,
            TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<SystemdDbusException>(
            () => transport.GetManagerVersionAsync(CancellationToken.None));

        Assert.Equal(SystemdDbusFailureKind.Timeout, exception.FailureKind);
        Assert.True(protocol.ObservedCancellation);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellationAndAbortsOutstandingCall()
    {
        var protocol = new FakeSystemdDbusProtocol
        {
            WaitForManagerVersionCancellation = true,
        };
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetManagerVersionAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.True(protocol.ObservedCancellation);
        Assert.True(protocol.Disposed);
    }

    [Fact]
    public async Task FabricatedUnitReferenceIsRejectedBeforeProtocolCall()
    {
        var protocol = new FakeSystemdDbusProtocol();
        await using var transport = new SystemdDbusTransport(protocol, TimeSpan.FromSeconds(5));
        var fabricatedUnit = new SystemdUnitReference();

        await Assert.ThrowsAsync<ArgumentException>(
            () => transport.ReadUnitPropertiesAsync(
                fabricatedUnit,
                TestContext.Current.CancellationToken));

        Assert.Null(protocol.ObservedObjectPath);
    }

    [Fact]
    public async Task UnitReferenceFromAnotherTransportIsRejectedBeforeProtocolCall()
    {
        var firstProtocol = new FakeSystemdDbusProtocol { ListedUnits = [ListedUnit] };
        var secondProtocol = new FakeSystemdDbusProtocol();
        await using var first = new SystemdDbusTransport(firstProtocol, TimeSpan.FromSeconds(5));
        await using var second = new SystemdDbusTransport(secondProtocol, TimeSpan.FromSeconds(5));
        var unit = Assert.Single(
            await first.ListServiceUnitsAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(
            () => second.ReadUnitPropertiesAsync(
                unit.Unit,
                TestContext.Current.CancellationToken));

        Assert.Null(secondProtocol.ObservedObjectPath);
    }

    [Fact]
    public void TransportContractDoesNotAcceptArbitraryDbusIdentifiers()
    {
        var forbiddenParameterTypes = new[] { typeof(string), typeof(Uri) };

        var parameters = typeof(ISystemdDbusTransport)
            .GetMethods()
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(parameters, forbiddenParameterTypes.Contains);
    }

    private sealed class FakeSystemdDbusProtocol : ISystemdDbusProtocol
    {
        public string ManagerVersion { get; init; } = "systemd 255";

        public Exception? ManagerVersionFailure { get; init; }

        public bool WaitForManagerVersionCancellation { get; init; }

        public ProtocolUnitFileEntry[] UnitFiles { get; init; } = [];

        public ProtocolListedUnit[] ListedUnits { get; init; } = [];

        public ProtocolUnitProperties UnitProperties { get; init; } = new(
            "alpha.service",
            ["alpha.service"],
            "Alpha",
            "loaded",
            "active",
            "running");

        public ProtocolEnvironmentProperties EnvironmentProperties { get; init; } = new(
            "alpha.service", ["alpha.service"], "loaded", "/etc/systemd/system/alpha.service",
            [], false, false, "enabled", [], [], [], []);

        public string[] ObservedStates { get; private set; } = [];

        public string[] ObservedPatterns { get; private set; } = [];

        public string[] ObservedNames { get; private set; } = [];

        public string? ObservedObjectPath { get; private set; }

        public bool ObservedCancellation { get; private set; }

        public bool Disposed { get; private set; }

        public async Task<string> GetManagerVersionAsync(CancellationToken cancellationToken)
        {
            if (ManagerVersionFailure is not null)
            {
                throw ManagerVersionFailure;
            }

            if (WaitForManagerVersionCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ManagerVersion;
        }

        public Task<ProtocolUnitFileEntry[]> ListUnitFilesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(UnitFiles);

        public Task<ProtocolListedUnit[]> ListUnitsByPatternsAsync(
            string[] states,
            string[] patterns,
            CancellationToken cancellationToken)
        {
            ObservedStates = states;
            ObservedPatterns = patterns;
            return Task.FromResult(ListedUnits);
        }

        public Task<ProtocolListedUnit[]> ListUnitsByNamesAsync(
            string[] names,
            CancellationToken cancellationToken)
        {
            ObservedNames = names;
            return Task.FromResult(ListedUnits);
        }

        public Task<ProtocolUnitProperties> ReadUnitPropertiesAsync(
            string objectPath,
            CancellationToken cancellationToken)
        {
            ObservedObjectPath = objectPath;
            return Task.FromResult(UnitProperties);
        }

        public Task<ProtocolEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            string objectPath,
            CancellationToken cancellationToken)
        {
            ObservedObjectPath = objectPath;
            return Task.FromResult(EnvironmentProperties);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
