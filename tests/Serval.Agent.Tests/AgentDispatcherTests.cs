using Microsoft.Data.Sqlite;
using Serval.Agent;
using Serval.Application.Authorization;
using Serval.Application.Ipc;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Infrastructure.Policy;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class AgentDispatcherTests
{
    [Fact]
    public async Task ListFiltersProtectedAndUngrantableServicesBeforePublication()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services =
        [
            Service("alpha.service"),
            Service("hidden.service", "ssh.service"),
            Service("beta.service"),
            Service("serval-agent.service"),
        ];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);

        var adminSession = await fixture.LoginAsync("admin");
        var empty = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("a", await fixture.LoginAsync("alice")),
            TestContext.Current.CancellationToken));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<AgentService>>(empty.Services));

        var adminList = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("b", adminSession), TestContext.Current.CancellationToken));
        Assert.Equal(["alpha.service", "beta.service"], adminList.Services!.Select(item => item.Id));

        var hidden = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("c", adminSession, "hidden.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, hidden.Code);
        Assert.Null(hidden.Service);
    }

    [Fact]
    public async Task CurrentGroupGrantAllowsCanonicalAndAliasButRemovedMembershipDenies()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service", "worker-alias.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002, new LinuxGroup("operators", 500));
        fixture.Identities.Groups["operators"] = new LinuxGroup("operators", 500);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");

        var deniedWrite = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("d", alice, "Group", "operators", "Service.View", "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, deniedWrite.Code);

        var aliasGrant = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("e", admin, "Group", "operators", "Service.View", "worker-alias.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, aliasGrant.Code);

        var grant = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("f", admin, "Group", "operators", "Service.View", "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, grant.Code);

        var permitted = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("g", alice, "worker-alias.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal("worker.service", permitted.Service?.Id);

        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var afterRemoval = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("h", alice, "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, afterRemoval.Code);
        Assert.Null(afterRemoval.Service);

        fixture.Identities.Accounts["alice"] = Account("alice", 1002, new LinuxGroup("operators", 501));
        var reusedName = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("h2", alice, "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, reusedName.Code);
    }

    [Fact]
    public async Task UserGrantRequiresOriginalNameAndUidAndCurrentNssResult()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker@Alpha.service"), Service("worker@alpha.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");

        var grant = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("u", admin, "User", "alice", "Service.View", "worker@Alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, grant.Code);

        var allowed = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("v", alice, "worker@Alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, allowed.Code);
        var caseDistinct = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("w", alice, "worker@alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, caseDistinct.Code);

        fixture.Identities.Accounts["alice"] = Account("alice", 2002);
        var replacement = await fixture.LoginAsync("alice");
        var reusedName = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("x", replacement, "worker@Alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, reusedName.Code);

        fixture.Identities.Accounts["bob"] = Account("bob", 1002);
        var reusedUid = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("y", await fixture.LoginAsync("bob"), "worker@Alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, reusedUid.Code);

        fixture.Identities.FailNss = true;
        var nssFailure = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("z", replacement, "worker@Alpha.service"),
            TestContext.Current.CancellationToken));
        Assert.NotEqual(AgentResultCode.Success, nssFailure.Code);
        Assert.Null(nssFailure.Service);
    }

    [Fact]
    public async Task ForgedSessionAndPolicyOutageNeverReleaseMetadata()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");

        var forged = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("i", "forged"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unauthenticated, forged.Code);
        Assert.Null(forged.Services);
        Assert.Equal(0, fixture.Inventory.ListCalls);

        fixture.Inventory.ThrowOnList = true;
        var systemdFailure = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("i2", admin), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unavailable, systemdFailure.Code);
        Assert.Null(systemdFailure.Services);
        Assert.Equal("Unavailable", fixture.AuditOutcome("i2"));
        Assert.Equal(("admin", (long?)1001), fixture.AuditActor("i2"));
        fixture.Inventory.ThrowOnList = false;

        fixture.PolicyFacade.ThrowOnList = true;
        var grantFailure = Assert.IsType<AgentResult.ListGrants>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListGrants("i2-grants", admin), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unavailable, grantFailure.Code);
        Assert.Null(grantFailure.Grants);
        Assert.Equal("Unavailable", fixture.AuditOutcome("i2-grants"));
        Assert.Equal(("admin", (long?)1001), fixture.AuditActor("i2-grants"));
        fixture.PolicyFacade.ThrowOnList = false;

        var invalid = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("i3", admin, "../worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.InvalidRequest, invalid.Code);
        Assert.Null(invalid.Service);

        var directProtected = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("i4", admin, "ssh.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, directProtected.Code);
        Assert.Null(directProtected.Service);

        File.Delete(fixture.DatabasePath);
        var outage = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("j", admin), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unavailable, outage.Code);
        Assert.Null(outage.Services);
        Assert.Equal(1, fixture.Inventory.ListCalls);
    }

    [Fact]
    public async Task AuditNeverPersistsPasswordOrSessionCredential()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service"), Service("hidden.service", "ssh.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var login = Assert.IsType<AgentResult.Login>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.Login("k", "admin", "unique-password-marker"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, login.Code);

        var session = login.Session!;
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.ListServices("k-list", session),
            TestContext.Current.CancellationToken);
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.InspectService(
            "k-inspect", session, "worker.service"), TestContext.Current.CancellationToken);
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.InspectService(
            "k-protected", session, "ssh.service"), TestContext.Current.CancellationToken);
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.InspectService(
            "k-protected-alias", session, "hidden.service"), TestContext.Current.CancellationToken);
        var added = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("k-add", session, "User", "alice", "Service.View", "worker.service"),
            TestContext.Current.CancellationToken));
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.RemoveGrant(
            "k-remove", session, added.GrantId!.Value), TestContext.Current.CancellationToken);
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.Logout("k-logout", session),
            TestContext.Current.CancellationToken);

        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT actor_name, operation, service, outcome, correlation_id FROM audit ORDER BY id";
            using var reader = command.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read())
            {
                Assert.InRange(reader.GetString(1).Length, 1, 32);
                Assert.InRange(reader.GetString(3).Length, 1, 32);
                Assert.InRange(reader.GetString(4).Length, 1, 64);
                rows.Add(string.Join('|', Enumerable.Range(0, 5)
                    .Select(index => reader.IsDBNull(index) ? string.Empty : reader.GetString(index))));
            }

            var contents = string.Join('\n', rows);
            Assert.DoesNotContain("unique-password-marker", contents, StringComparison.Ordinal);
            Assert.DoesNotContain(login.Session!, contents, StringComparison.Ordinal);
            Assert.DoesNotContain("Synthetic service", contents, StringComparison.Ordinal);
            Assert.Contains("Login", contents, StringComparison.Ordinal);
            Assert.Contains("Logout", contents, StringComparison.Ordinal);
            Assert.Contains("AddGrant", contents, StringComparison.Ordinal);
            Assert.Contains("RemoveGrant", contents, StringComparison.Ordinal);
            Assert.Contains("ListServices", contents, StringComparison.Ordinal);
            Assert.Contains("InspectService", contents, StringComparison.Ordinal);
            reader.Close();
            command.CommandText = "SELECT service FROM audit WHERE correlation_id = $correlation";
            command.Parameters.AddWithValue("$correlation", "k-remove");
            Assert.Equal("worker.service", command.ExecuteScalar());
            command.Parameters["$correlation"].Value = "k-protected";
            Assert.Equal("ssh.service", command.ExecuteScalar());
            command.Parameters["$correlation"].Value = "k-protected-alias";
            Assert.Equal("hidden.service", command.ExecuteScalar());
        }
    }

    [Fact]
    public async Task RejectedRequestsDoNotExtendIdleSessionButAcceptedReadsDo()
    {
        var time = new ManualTimeProvider();
        using var fixture = new Fixture(time);
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);

        var rejectedSession = await fixture.LoginAsync("alice");
        time.Advance(TimeSpan.FromMinutes(14));
        var rejected = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("idle-denied", rejectedSession, "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, rejected.Code);
        time.Advance(TimeSpan.FromMinutes(1));
        var expired = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("idle-expired", rejectedSession),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unauthenticated, expired.Code);

        var acceptedSession = await fixture.LoginAsync("alice");
        time.Advance(TimeSpan.FromMinutes(14));
        var accepted = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("idle-accepted", acceptedSession),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, accepted.Code);
        time.Advance(TimeSpan.FromMinutes(14));
        var stillActive = Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices("idle-active", acceptedSession),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, stillActive.Code);
    }

    [Fact]
    public async Task GrantAdministrationValidatesEveryTargetAndTakesEffectImmediately()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service", "worker-alias.service"), Service("ssh.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");

        var unauthorizedList = Assert.IsType<AgentResult.ListGrants>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListGrants("ga", alice), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, unauthorizedList.Code);
        Assert.Null(unauthorizedList.Grants);

        foreach (var invalid in new[]
        {
            new AgentRequest.AddGrant("gb", admin, "User", "missing", "Service.View", "worker.service"),
            new AgentRequest.AddGrant("gc", admin, "User", "../alice", "Service.View", "worker.service"),
            new AgentRequest.AddGrant("gd", admin, "User", "alice", "Service.Restart", "worker.service"),
            new AgentRequest.AddGrant("ge", admin, "User", "alice", "Service.View", "worker-alias.service"),
            new AgentRequest.AddGrant("gf", admin, "User", "alice", "Service.View", "worker@.service"),
            new AgentRequest.AddGrant("gg", admin, "User", "alice", "Service.View", "ssh.service"),
        })
        {
            var denied = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
                invalid, TestContext.Current.CancellationToken));
            Assert.NotEqual(AgentResultCode.Success, denied.Code);
            Assert.Null(denied.GrantId);
        }

        Assert.Empty(fixture.Policy.ListGrants());
        var added = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("gh", admin, "User", "alice", "Service.View", "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, added.Code);
        var listed = Assert.IsType<AgentResult.ListGrants>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListGrants("gi", admin), TestContext.Current.CancellationToken));
        Assert.Equal(added.GrantId, Assert.Single(listed.Grants!).Id);

        var permitted = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("gj", alice, "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, permitted.Code);

        var unauthorizedRemove = Assert.IsType<AgentResult.RemoveGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RemoveGrant("gk", alice, added.GrantId!.Value),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, unauthorizedRemove.Code);
        Assert.Single(fixture.Policy.ListGrants());

        var removed = Assert.IsType<AgentResult.RemoveGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RemoveGrant("gl", admin, added.GrantId.Value),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, removed.Code);
        Assert.Empty(fixture.Policy.ListGrants());

        var afterRemoval = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("gm", alice, "worker.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, afterRemoval.Code);
    }

    [Fact]
    public async Task InspectionValidatesBeforeSystemdAndKeepsEscapedInstancesDistinct()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services =
        [
            Service("worker@foo\\x2dbar.service"),
            Service("worker@foo-bar.service"),
        ];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        _ = await fixture.Dispatcher.DispatchAsync(new AgentRequest.AddGrant(
            "ia", admin, "User", "alice", "Service.View", "worker@foo\\x2dbar.service"),
            TestContext.Current.CancellationToken);

        var escaped = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("ib", alice, "worker@foo\\x2dbar.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, escaped.Code);
        var plain = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("ic", alice, "worker@foo-bar.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Denied, plain.Code);
        Assert.Null(plain.Service);

        var callsBeforeInvalid = fixture.Inventory.InspectCalls;
        foreach (var invalid in new[] { "../worker.service", "worker@.service", "-bad/.service" })
        {
            var result = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
                new AgentRequest.InspectService("id", admin, invalid), TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.InvalidRequest, result.Code);
            Assert.Null(result.Service);
        }

        Assert.Equal(callsBeforeInvalid, fixture.Inventory.InspectCalls);
        var absent = Assert.IsType<AgentResult.InspectService>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.InspectService("ie", admin, "absent.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, absent.Code);
        Assert.Null(absent.Service);
    }

    private static LinuxAccount Account(string name, uint uid, params LinuxGroup[] groups) =>
        new(name, uid, 500, groups);

    private static SystemServiceIdentity Service(string canonical, params string[] aliases)
    {
        var id = new SystemServiceId(canonical);
        var service = new SystemService(id, "Synthetic service", new SystemdLoadState("loaded"),
            new SystemdActiveState("active"), new SystemdSubState("running"));
        var names = aliases.Append(canonical).OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new SystemServiceId(name)).ToArray();
        return new SystemServiceIdentity(service, names);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"serval-dispatch-{Guid.NewGuid():N}");

        public Fixture(TimeProvider? timeProvider = null)
        {
            Directory.CreateDirectory(directory);
            DatabasePath = Path.Combine(directory, "policy.db");
            Policy = new AgentPolicyStore(DatabasePath);
            Policy.Initialize();
            PolicyFacade = new TestPolicyStore(Policy);
            Identities = new FakeIdentities();
            Inventory = new FakeInventory();
            var clock = timeProvider ?? TimeProvider.System;
            var sessions = new AgentSessions(TimeSpan.FromMinutes(15), Identities, clock);
            Dispatcher = new AgentDispatcher(new AgentConfiguration(2000, 2000, "sudo", 27,
                TimeSpan.FromMinutes(15)), Identities, sessions, PolicyFacade, Inventory, clock);
        }

        public string DatabasePath { get; }

        public AgentPolicyStore Policy { get; }

        public TestPolicyStore PolicyFacade { get; }

        public FakeIdentities Identities { get; }

        public FakeInventory Inventory { get; }

        public AgentDispatcher Dispatcher { get; }

        public string? AuditOutcome(string correlation)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT outcome FROM audit WHERE correlation_id = $correlation ORDER BY id DESC LIMIT 1";
            command.Parameters.AddWithValue("$correlation", correlation);
            return command.ExecuteScalar() as string;
        }

        public (string? Name, long? Uid) AuditActor(string correlation)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT actor_name, actor_uid FROM audit WHERE correlation_id = $correlation ORDER BY id DESC LIMIT 1";
            command.Parameters.AddWithValue("$correlation", correlation);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            return (reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1));
        }

        public async Task<string> LoginAsync(string name)
        {
            var result = Assert.IsType<AgentResult.Login>(await Dispatcher.DispatchAsync(
                new AgentRequest.Login("login", name, "synthetic"), TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.Success, result.Code);
            return Assert.IsType<string>(result.Session);
        }

        public void Dispose()
        {
            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    private sealed class TestPolicyStore(AgentPolicyStore inner) : IAgentPolicyStore
    {
        public bool ThrowOnList { get; set; }

        public IReadOnlyList<StoredGrant> ListGrants() =>
            ThrowOnList ? throw new InvalidOperationException("Synthetic policy read failure") : inner.ListGrants();

        public long AddGrant(StoredGrant grant, AuditEntry audit) => inner.AddGrant(grant, audit);

        public bool RemoveGrant(long id, AuditEntry audit) => inner.RemoveGrant(id, audit);

        public void AppendAudit(AuditEntry audit) => inner.AppendAudit(audit);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan interval) => ticks += interval.Ticks;
    }

    private sealed class FakeIdentities : IAgentIdentityChecks
    {
        public Dictionary<string, LinuxAccount> Accounts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, LinuxGroup> Groups { get; } = new(StringComparer.Ordinal);

        public bool FailNss { get; set; }

        public Task<AgentAuthentication> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
        {
            var account = Accounts.GetValueOrDefault(username);
            return Task.FromResult(account is null
                ? new AgentAuthentication(AgentResultStatus.Denied, null)
                : new AgentAuthentication(AgentResultStatus.Success, account));
        }

        public Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.TryGetValue(name, out var account) && account.Uid == uid);

        public Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(FailNss ? null : Accounts.GetValueOrDefault(username));

        public Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Values.FirstOrDefault(account => account.Uid == uid));

        public Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken) =>
            Task.FromResult(Groups.GetValueOrDefault(groupName));
    }

    private sealed class FakeInventory : ISystemServiceIdentityInventory
    {
        public IReadOnlyList<SystemServiceIdentity> Services { get; set; } = [];

        public int ListCalls { get; private set; }

        public int InspectCalls { get; private set; }

        public bool ThrowOnList { get; set; }

        public Task<IReadOnlyList<SystemServiceIdentity>> ListIdentitiesAsync(CancellationToken cancellationToken)
        {
            ListCalls++;
            if (ThrowOnList)
            {
                throw new InvalidOperationException("Synthetic systemd failure");
            }

            return Task.FromResult(Services);
        }

        public Task<ServiceIdentityInspectionResult> InspectIdentityAsync(SystemServiceId serviceId,
            CancellationToken cancellationToken)
        {
            InspectCalls++;
            var identity = Services.FirstOrDefault(item => item.Names.Contains(serviceId));
            ServiceIdentityInspectionResult result = identity is null
                ? new ServiceIdentityInspectionResult.NotFound(serviceId)
                : new ServiceIdentityInspectionResult.Found(identity);
            return Task.FromResult(result);
        }
    }
}
