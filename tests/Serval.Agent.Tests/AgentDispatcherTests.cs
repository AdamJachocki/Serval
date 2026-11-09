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
    [Theory]
    [InlineData(EnvironmentReadFailureCode.NotFound)]
    [InlineData(EnvironmentReadFailureCode.ProtectedTarget)]
    public async Task TargetDisappearanceDuringAcquisitionRemainsIndistinguishable(EnvironmentReadFailureCode failure)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var session = await fixture.LoginAsync("admin");
        fixture.Environment.Failure = failure;
        using var result = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("target-race", session, "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, result.Code);
        Assert.Null(result.Variable);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task UnexpectedReaderFailureRetainsKnownAuditIdentityAndCanonicalService()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service", "alias.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var session = await fixture.LoginAsync("admin");
        fixture.Environment.DuringRead = () => throw new InvalidOperationException("Synthetic read failure.");
        using var result = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("reader-error", session, "alias.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unavailable, result.Code);
        Assert.Equal(("admin", 1001L), fixture.AuditActor("reader-error"));
        using var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT service FROM audit WHERE correlation_id='reader-error'";
        Assert.Equal("worker.service", command.ExecuteScalar());
    }

    [Theory]
    [InlineData("Environment.View")]
    [InlineData("Environment.Reveal")]
    public async Task EnvironmentGrantsCanBeRemovedOnlyByCurrentAdministrator(string operation)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        var added = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("add-env", admin, "User", "alice", operation, "worker.service"), TestContext.Current.CancellationToken));
        Assert.Equal(operation, Assert.Single(fixture.Policy.ListGrants()).Operation);
        var denied = await fixture.Dispatcher.DispatchAsync(new AgentRequest.RemoveGrant("deny-env", alice, added.GrantId!.Value), TestContext.Current.CancellationToken);
        Assert.Equal(AgentResultCode.Denied, denied.Code);
        Assert.Single(fixture.Policy.ListGrants());
        var removed = await fixture.Dispatcher.DispatchAsync(new AgentRequest.RemoveGrant("remove-env", admin, added.GrantId.Value), TestContext.Current.CancellationToken);
        Assert.Equal(AgentResultCode.Success, removed.Code);
        Assert.Empty(fixture.Policy.ListGrants());
    }

    [Fact]
    public async Task EnvironmentReadsFreshValuesAndSourcesAndDistinguishesEmptyFromMissing()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var session = await fixture.LoginAsync("admin");
        var listed = Assert.IsType<AgentResult.ListEnvironment>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListEnvironment("metadata", session, "worker.service"), TestContext.Current.CancellationToken));
        Assert.True(listed.Environment!.CanReveal);
        Assert.Equal(0, Assert.Single(listed.Environment.Variables).WinningSourceId);
        fixture.Environment.Source = 1;
        fixture.Environment.Value = "";
        using var revealed = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("empty", session, "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, revealed.Code);
        Assert.Equal(1, revealed.Variable!.Source.Id);
        using var http = new MemoryStream();
        await revealed.Variable.WriteHttpAsync(http, TestContext.Current.CancellationToken);
        using var json = System.Text.Json.JsonDocument.Parse(http.ToArray());
        Assert.True(json.RootElement.GetProperty("value").GetString() == "");
        using var absent = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("missing", session, "worker.service", "ABSENT"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.NotFound, absent.Code);
        Assert.Null(absent.Variable);
        fixture.Environment.Empty = true;
        var empty = Assert.IsType<AgentResult.ListEnvironment>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListEnvironment("empty-metadata", session, "worker.service"), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, empty.Code);
        Assert.Empty(empty.Environment!.Variables);
        Assert.Equal(4, fixture.Environment.Calls);
    }

    [Theory]
    [InlineData(EnvironmentReadFailureCode.Timeout)]
    [InlineData(EnvironmentReadFailureCode.InconsistentSnapshot)]
    [InlineData(EnvironmentReadFailureCode.UnsupportedConfiguration)]
    public async Task FailedEnvironmentReadsPublishNoPartialData(EnvironmentReadFailureCode failure)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var session = await fixture.LoginAsync("admin");
        fixture.Environment.Failure = failure;
        var listed = Assert.IsType<AgentResult.ListEnvironment>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListEnvironment("failed-list", session, "worker.service"), TestContext.Current.CancellationToken));
        Assert.Null(listed.Environment);
        Assert.Equal(failure.ToString(), listed.Failure);
        using var revealed = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("failed-reveal", session, "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Null(revealed.Variable);
        Assert.Equal(AgentResultCode.Unavailable, revealed.Code);
        Assert.Equal("Unavailable", fixture.AuditOutcome("failed-reveal"));
    }

    [Theory]
    [InlineData("User", "Service.View", false, false)]
    [InlineData("User", "Environment.View", true, false)]
    [InlineData("User", "Environment.Reveal", false, false)]
    [InlineData("Group", "Environment.View", true, false)]
    [InlineData("Group", "Both", true, true)]
    [InlineData("User", "Both", true, true)]
    public async Task EnvironmentPermissionsAreIndependent(string kind, string operation, bool listAllowed, bool revealAllowed)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service", "alias.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002, new LinuxGroup("operators", 500));
        fixture.Identities.Groups["operators"] = new LinuxGroup("operators", 500);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        var operations = operation == "Both" ? new[] { "Environment.View", "Environment.Reveal" } : [operation];
        foreach (var permission in operations)
        {
            var granted = await fixture.Dispatcher.DispatchAsync(new AgentRequest.AddGrant("env-grant", admin,
                kind, kind == "User" ? "alice" : "operators", permission, "worker.service"), TestContext.Current.CancellationToken);
            Assert.Equal(AgentResultCode.Success, granted.Code);
        }
        var listed = Assert.IsType<AgentResult.ListEnvironment>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListEnvironment("env-list", alice, "alias.service"), TestContext.Current.CancellationToken));
        Assert.Equal(listAllowed ? AgentResultCode.Success : AgentResultCode.Denied, listed.Code);
        Assert.Equal(listAllowed ? 1 : 0, fixture.Environment.Calls);
        using var revealed = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("env-reveal", alice, "alias.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Equal(revealAllowed ? AgentResultCode.Success : AgentResultCode.Denied, revealed.Code);
        Assert.Equal((listAllowed ? 1 : 0) + (revealAllowed ? 1 : 0), fixture.Environment.Calls);
        if (kind == "Group")
        {
            fixture.Identities.Accounts["alice"] = Account("alice", 1002);
            var denied = await fixture.Dispatcher.DispatchAsync(new AgentRequest.ListEnvironment("removed", alice, "worker.service"), TestContext.Current.CancellationToken);
            Assert.Equal(AgentResultCode.Denied, denied.Code);
        }
    }

    [Theory]
    [InlineData("../worker.service", "CONFIG", AgentResultCode.InvalidRequest)]
    [InlineData("worker@.service", "CONFIG", AgentResultCode.InvalidRequest)]
    [InlineData("worker.service", "../CONFIG", AgentResultCode.InvalidRequest)]
    [InlineData("ssh.service", "CONFIG", AgentResultCode.NotFound)]
    [InlineData("hidden.service", "CONFIG", AgentResultCode.NotFound)]
    [InlineData("absent.service", "CONFIG", AgentResultCode.NotFound)]
    public async Task EnvironmentRejectsUnsafeTargetsBeforeAcquisition(string service, string name, AgentResultCode expected)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service"), Service("hidden.service", "ssh.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        using var result = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("unsafe", admin, service, name), TestContext.Current.CancellationToken));
        Assert.Equal(expected, result.Code);
        Assert.Null(result.Variable);
        Assert.Equal(0, fixture.Environment.Calls);
    }

    [Theory]
    [InlineData("membership", AgentResultCode.Denied)]
    [InlineData("identity", AgentResultCode.Unauthenticated)]
    [InlineData("protection", AgentResultCode.NotFound)]
    [InlineData("mismatch", AgentResultCode.Unavailable)]
    [InlineData("audit", AgentResultCode.Unavailable)]
    public async Task EnvironmentRechecksBeforePublishing(string change, AgentResultCode expected)
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("worker.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var session = await fixture.LoginAsync("admin");
        fixture.Environment.DuringRead = () =>
        {
            if (change == "membership") fixture.Identities.Accounts["admin"] = Account("admin", 1001);
            if (change == "identity") fixture.Identities.Accounts.Remove("admin");
            if (change == "protection") fixture.Inventory.Services = [Service("worker.service", "ssh.service")];
            if (change == "mismatch") fixture.Environment.Canonical = "different.service";
            if (change == "audit") fixture.PolicyFacade.ThrowOnAudit = true;
        };
        using var result = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RevealEnvironmentVariable("recheck", session, "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
        Assert.Equal(expected, result.Code);
        Assert.Null(result.Variable);
        Assert.Equal(1, fixture.Environment.Calls);
    }

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

    [Fact]
    public async Task ListFavoritesStayWithinAuthorizedUnprotectedOutput()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services =
        [
            Service("a.service"), Service("b.service"), Service("c.service"),
            Service("hidden.service", "ssh.service"),
        ];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        foreach (var service in new[] { "a.service", "b.service" })
        {
            await GrantViewAsync(fixture, admin, "alice", service);
        }

        foreach (var stored in new[] { "a.service", "c.service", "hidden.service", "gone.service" })
        {
            fixture.Policy.TryAdd("alice", 1002, stored, 100, StoreAudit("AddFavorite"));
        }

        fixture.Policy.TryAdd("bob", 1003, "b.service", 100, StoreAudit("AddFavorite"));

        var list = await ListAsync(fixture, alice, "fav-list");
        Assert.Equal(["a.service", "b.service"], list.Services!.Select(item => item.Id));
        Assert.Equal(["a.service"], list.Favorites);
    }

    [Fact]
    public async Task RevokedGrantHidesFavoriteAndReGrantRestoresIt()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        var grantId = await GrantViewAsync(fixture, admin, "alice", "a.service");
        Assert.Equal(AgentResultCode.Success, (await AddFavoriteAsync(fixture, alice, "a.service", "fav-add")).Code);
        Assert.Equal(["a.service"], (await ListAsync(fixture, alice, "fav-l1")).Favorites);

        Assert.Equal(AgentResultCode.Success, (await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RemoveGrant("fav-revoke", admin, grantId), TestContext.Current.CancellationToken)).Code);
        var hidden = await ListAsync(fixture, alice, "fav-l2");
        Assert.Empty(hidden.Services!);
        Assert.Empty(hidden.Favorites!);

        await GrantViewAsync(fixture, admin, "alice", "a.service");
        Assert.Equal(["a.service"], (await ListAsync(fixture, alice, "fav-l3")).Favorites);
    }

    [Fact]
    public async Task ListServicesFavoriteStoreFailureYieldsUnavailableWithoutPartialData()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        fixture.PolicyFacade.ThrowOnFavorites = true;

        var list = await ListAsync(fixture, admin, "fav-list-fail");

        Assert.Equal(AgentResultCode.Unavailable, list.Code);
        Assert.Null(list.Services);
        Assert.Null(list.Favorites);
        Assert.Equal("Unavailable", fixture.AuditOutcome("fav-list-fail"));
    }

    [Fact]
    public async Task AddFavoriteStoresCanonicalIdentityOnceAndAuditsIt()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service", "alias.service")];
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        await GrantViewAsync(fixture, admin, "alice", "a.service");

        var viaAlias = await AddFavoriteAsync(fixture, alice, "alias.service", "fav-alias");
        var direct = await AddFavoriteAsync(fixture, alice, "a.service", "fav-direct");

        Assert.Equal(AgentResultCode.Success, viaAlias.Code);
        Assert.Equal(AgentResultCode.Success, direct.Code);
        Assert.Equal(["a.service"], fixture.Policy.ListFavorites("alice", 1002));
        Assert.Equal("Success", fixture.AuditOutcome("fav-alias"));
        Assert.Equal(("alice", 1002L), fixture.AuditActor("fav-alias"));
        Assert.Equal(("AddFavorite", "a.service"), fixture.AuditOperationAndService("fav-alias"));
    }

    [Fact]
    public async Task AddFavoriteRejectsUnauthorizedProtectedAndMissingTargetsIdentically()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services =
        [
            Service("allowed.service"), Service("other.service"),
            Service("hidden.service", "ssh.service"),
        ];
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        await GrantViewAsync(fixture, admin, "alice", "allowed.service");

        foreach (var target in new[] { "other.service", "ssh.service", "hidden.service", "absent.service" })
        {
            var result = await AddFavoriteAsync(fixture, alice, target, "fav-reject");
            Assert.Equal(AgentResultCode.NotFound, result.Code);
        }

        var adminProtected = await AddFavoriteAsync(fixture, admin, "hidden.service", "fav-admin-protected");
        Assert.Equal(AgentResultCode.NotFound, adminProtected.Code);
        Assert.Empty(fixture.Policy.ListFavorites("alice", 1002));
        Assert.Empty(fixture.Policy.ListFavorites("admin", 1001));
    }

    [Fact]
    public async Task AddFavoriteRejectsMalformedNamesAndBadSessionsBeforeAnyLookup()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        var inspectsBefore = fixture.Inventory.InspectCalls;

        foreach (var invalid in new[] { "worker@.service", "../a.service", "-bad/.service", "a service" })
        {
            Assert.Equal(AgentResultCode.InvalidRequest,
                (await AddFavoriteAsync(fixture, admin, invalid, "fav-invalid")).Code);
        }

        Assert.Equal(AgentResultCode.Unauthenticated,
            (await AddFavoriteAsync(fixture, "forged-session", "a.service", "fav-forged")).Code);
        Assert.Equal(inspectsBefore, fixture.Inventory.InspectCalls);
        Assert.Empty(fixture.Policy.ListFavorites("admin", 1001));
        Assert.Equal("Unauthenticated", fixture.AuditOutcome("fav-forged"));
    }

    [Fact]
    public async Task AddFavoriteRejectsBeyondTheLimitButAcceptsExistingOnes()
    {
        using var fixture = new Fixture();
        var services = Enumerable.Range(0, 101).Select(index => Service($"s{index:D3}.service")).ToArray();
        fixture.Inventory.Services = services;
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(AgentResultCode.Success,
                (await AddFavoriteAsync(fixture, admin, $"s{index:D3}.service", "fav-fill")).Code);
        }

        var rejected = await AddFavoriteAsync(fixture, admin, "s100.service", "fav-limit");
        var existing = await AddFavoriteAsync(fixture, admin, "s042.service", "fav-existing");

        Assert.Equal(AgentResultCode.LimitReached, rejected.Code);
        Assert.Equal("LimitReached", fixture.AuditOutcome("fav-limit"));
        Assert.Equal(AgentResultCode.Success, existing.Code);
        var stored = fixture.Policy.ListFavorites("admin", 1001);
        Assert.Equal(100, stored.Count);
        Assert.DoesNotContain("s100.service", stored);
    }

    [Fact]
    public async Task RemoveFavoriteReturnsIdenticalSuccessAndTouchesOnlyTheCaller()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services =
        [
            Service("a.service", "alias.service"), Service("other.service"),
            Service("hidden.service", "ssh.service"),
        ];
        fixture.Identities.Accounts["alice"] = Account("alice", 1002);
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        var alice = await fixture.LoginAsync("alice");
        await GrantViewAsync(fixture, admin, "alice", "a.service");
        await AddFavoriteAsync(fixture, alice, "a.service", "fav-seed");
        fixture.Policy.TryAdd("admin", 1001, "a.service", 100, StoreAudit("AddFavorite"));
        fixture.Policy.TryAdd("alice", 1002, "gone.service", 100, StoreAudit("AddFavorite"));

        var results = new List<AgentResultCode>();
        foreach (var target in new[] { "alias.service", "a.service", "other.service", "ssh.service",
            "hidden.service", "absent.service", "gone.service" })
        {
            results.Add((await RemoveFavoriteAsync(fixture, alice, target, "fav-remove-" + target.Replace('.', '-'))).Code);
        }

        Assert.All(results, code => Assert.Equal(AgentResultCode.Success, code));
        Assert.Empty(fixture.Policy.ListFavorites("alice", 1002));
        Assert.Equal(["a.service"], fixture.Policy.ListFavorites("admin", 1001));
        Assert.Equal(("RemoveFavorite", "a.service"), fixture.AuditOperationAndService("fav-remove-alias-service"));
        foreach (var unresolved in new[] { "other.service", "ssh.service", "hidden.service", "absent.service", "gone.service" })
        {
            Assert.Equal(("RemoveFavorite", null), fixture.AuditOperationAndService("fav-remove-" + unresolved.Replace('.', '-')));
        }
    }

    [Fact]
    public async Task RemoveFavoriteRejectsMalformedNamesAndBadSessions()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        fixture.Policy.TryAdd("admin", 1001, "a.service", 100, StoreAudit("AddFavorite"));

        foreach (var invalid in new[] { "worker@.service", "../a.service", "bad name" })
        {
            Assert.Equal(AgentResultCode.InvalidRequest,
                (await RemoveFavoriteAsync(fixture, admin, invalid, "fav-rm-invalid")).Code);
        }

        Assert.Equal(AgentResultCode.Unauthenticated,
            (await RemoveFavoriteAsync(fixture, "forged-session", "a.service", "fav-rm-forged")).Code);
        Assert.Equal(["a.service"], fixture.Policy.ListFavorites("admin", 1001));
    }

    [Fact]
    public async Task FavoriteOperationsFailClosedAndAuditWhenTheStoreFails()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");
        fixture.Policy.TryAdd("admin", 1001, "a.service", 100, StoreAudit("AddFavorite"));
        fixture.PolicyFacade.ThrowOnFavorites = true;

        Assert.Equal(AgentResultCode.Unavailable, (await AddFavoriteAsync(fixture, admin, "a.service", "fav-add-fail")).Code);
        Assert.Equal(AgentResultCode.Unavailable, (await RemoveFavoriteAsync(fixture, admin, "a.service", "fav-rm-fail")).Code);

        Assert.Equal("Unavailable", fixture.AuditOutcome("fav-add-fail"));
        Assert.Equal("Unavailable", fixture.AuditOutcome("fav-rm-fail"));
        Assert.Equal(("admin", 1001L), fixture.AuditActor("fav-add-fail"));
        fixture.PolicyFacade.ThrowOnFavorites = false;
        Assert.Equal(["a.service"], fixture.Policy.ListFavorites("admin", 1001));
    }

    [Fact]
    public async Task FavoriteOperationsReadNoEnvironmentData()
    {
        using var fixture = new Fixture();
        fixture.Inventory.Services = [Service("a.service")];
        fixture.Identities.Accounts["admin"] = Account("admin", 1001, new LinuxGroup("sudo", 27));
        var admin = await fixture.LoginAsync("admin");

        await AddFavoriteAsync(fixture, admin, "a.service", "fav-env-add");
        await RemoveFavoriteAsync(fixture, admin, "a.service", "fav-env-remove");

        Assert.Equal(0, fixture.Environment.Calls);
    }

    private static async Task<AgentResult.ListServices> ListAsync(Fixture fixture, string session, string correlation) =>
        Assert.IsType<AgentResult.ListServices>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.ListServices(correlation, session), TestContext.Current.CancellationToken));

    private static async Task<AgentResult.AddFavorite> AddFavoriteAsync(
        Fixture fixture, string session, string service, string correlation) =>
        Assert.IsType<AgentResult.AddFavorite>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddFavorite(correlation, session, service), TestContext.Current.CancellationToken));

    private static async Task<AgentResult.RemoveFavorite> RemoveFavoriteAsync(
        Fixture fixture, string session, string service, string correlation) =>
        Assert.IsType<AgentResult.RemoveFavorite>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.RemoveFavorite(correlation, session, service), TestContext.Current.CancellationToken));

    private static async Task<long> GrantViewAsync(Fixture fixture, string adminSession, string user, string service)
    {
        var added = Assert.IsType<AgentResult.AddGrant>(await fixture.Dispatcher.DispatchAsync(
            new AgentRequest.AddGrant("grant-" + Guid.NewGuid().ToString("N"), adminSession, "User", user, "Service.View", service),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, added.Code);
        return added.GrantId!.Value;
    }

    private static AuditEntry StoreAudit(string operation) =>
        new("seed", 1, operation, null, "Success", "seed", DateTimeOffset.UtcNow);

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
                TimeSpan.FromMinutes(15)), Identities, sessions, PolicyFacade, PolicyFacade, Inventory, clock, Environment);
        }

        public string DatabasePath { get; }

        public AgentPolicyStore Policy { get; }

        public TestPolicyStore PolicyFacade { get; }

        public FakeIdentities Identities { get; }

        public FakeInventory Inventory { get; }

        public AgentDispatcher Dispatcher { get; }

        public FakeEnvironment Environment { get; } = new();

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

        public (string Operation, string? Service) AuditOperationAndService(string correlation)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT operation, service FROM audit WHERE correlation_id = $correlation ORDER BY id DESC LIMIT 1";
            command.Parameters.AddWithValue("$correlation", correlation);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
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

    private sealed class TestPolicyStore(AgentPolicyStore inner) : IAgentPolicyStore, IAgentFavoriteStore
    {
        public bool ThrowOnList { get; set; }

        public bool ThrowOnFavorites { get; set; }

        public IReadOnlyList<string> ListFavorites(string name, uint uid) =>
            ThrowOnFavorites ? throw new InvalidOperationException("Synthetic favorites failure") : inner.ListFavorites(name, uid);

        public FavoriteAddOutcome TryAdd(string name, uint uid, string service, int limit, AuditEntry audit) =>
            ThrowOnFavorites ? throw new InvalidOperationException("Synthetic favorites failure") : inner.TryAdd(name, uid, service, limit, audit);

        public void Remove(string name, uint uid, string service, AuditEntry audit)
        {
            if (ThrowOnFavorites) throw new InvalidOperationException("Synthetic favorites failure");
            inner.Remove(name, uid, service, audit);
        }

        public bool ThrowOnAudit { get; set; }

        public IReadOnlyList<StoredGrant> ListGrants() =>
            ThrowOnList ? throw new InvalidOperationException("Synthetic policy read failure") : inner.ListGrants();

        public long AddGrant(StoredGrant grant, AuditEntry audit) => inner.AddGrant(grant, audit);

        public bool RemoveGrant(long id, AuditEntry audit) => inner.RemoveGrant(id, audit);

        public void AppendAudit(AuditEntry audit)
        {
            if (ThrowOnAudit) throw new InvalidOperationException("Audit unavailable.");
            inner.AppendAudit(audit);
        }
    }

    private sealed class FakeEnvironment : ISystemServiceEnvironmentReader
    {
        public int Calls { get; private set; }
        public Action? DuringRead { get; set; }
        public string Canonical { get; set; } = "worker.service";
        public string Value { get; set; } = Guid.NewGuid().ToString("N");
        public int Source { get; set; }
        public bool Empty { get; set; }
        public EnvironmentReadFailureCode? Failure { get; set; }

        public Task<ServiceEnvironmentReadResult> ReadAsync(SystemServiceId serviceId, CancellationToken cancellationToken)
        {
            Calls++;
            DuringRead?.Invoke();
            if (Failure is { } failure)
                return Task.FromResult<ServiceEnvironmentReadResult>(new ServiceEnvironmentReadResult.Failure(failure,
                    failure == EnvironmentReadFailureCode.UnsupportedConfiguration ? EnvironmentUnsupportedReason.UnsetEnvironment : null));
            ServiceEnvironmentReadResult result = new ServiceEnvironmentReadResult.Success(new SystemServiceId(Canonical),
                [new EnvironmentSourceMetadata(Source, Source == 0 ? EnvironmentSourceKind.ManagerEnvironment : EnvironmentSourceKind.EnvironmentFile)],
                Empty ? [] : [new EnvironmentVariableMetadata("CONFIG", Source)],
                new EnvironmentValues(Empty ? [] : new Dictionary<string, string> { ["CONFIG"] = Value }));
            return Task.FromResult(result);
        }
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
