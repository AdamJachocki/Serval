using Microsoft.Data.Sqlite;
using Serval.Agent;
using Serval.Application.Ipc;
using Serval.Domain.Services;
using Serval.Infrastructure.Policy;
using Serval.Systemd;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class RealSystemdAgentAuthorizationTests
{
    public static bool IsEnabled => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("SERVAL_REAL_SYSTEMD_TESTS") == "1" &&
        Environment.GetEnvironmentVariable("SERVAL_REQUIRE_REAL_SYSTEMD_TESTS") == "1";

    [Fact(Skip = "Requires disposable real-systemd fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task AgentFiltersRealSystemServicesAndNeverMutatesFixtureFiles()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        var instanceId = prefix["serval-enumeration-test-".Length..];
        var inactive = prefix + "-inactive.service";
        var alias = prefix + "-alias.service";
        var privileged = "serval-agent@" + instanceId + ".service";
        var privilegedAlias = "serval-exclusion-alias@" + instanceId + ".service";
        var lookalike = "serval-agent-helper@" + instanceId + ".service";
        var unitPath = "/run/systemd/system/" + inactive;
        var unitBytes = File.ReadAllBytes(unitPath);

        var directory = Path.Combine(Path.GetTempPath(), $"serval-real-agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "policy.db");
        try
        {
            var store = new AgentPolicyStore(databasePath);
            store.Initialize();
            var identities = new FixtureIdentities();
            var sessions = new AgentSessions(TimeSpan.FromMinutes(15), identities, TimeProvider.System);
            var dispatcher = new AgentDispatcher(
                new AgentConfiguration(2000, 2000, "sudo", 27, TimeSpan.FromMinutes(15)),
                identities, sessions, store, new SystemdServiceInventory(), TimeProvider.System);
            var admin = await LoginAsync(dispatcher, "admin");
            var alice = await LoginAsync(dispatcher, "alice");

            var adminList = Assert.IsType<AgentResult.ListServices>(await dispatcher.DispatchAsync(
                new AgentRequest.ListServices("real-list-admin", admin), TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.Success, adminList.Code);
            var adminIds = adminList.Services!.Select(service => service.Id).ToArray();
            Assert.Contains(inactive, adminIds);
            Assert.Contains(lookalike, adminIds);
            Assert.DoesNotContain("systemd-journald.service", adminIds);
            Assert.DoesNotContain(privileged, adminIds);
            Assert.DoesNotContain(privilegedAlias, adminIds);

            var denied = Assert.IsType<AgentResult.ListServices>(await dispatcher.DispatchAsync(
                new AgentRequest.ListServices("real-list-alice", alice), TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.Success, denied.Code);
            Assert.Empty(denied.Services!);

            var added = Assert.IsType<AgentResult.AddGrant>(await dispatcher.DispatchAsync(
                new AgentRequest.AddGrant("real-grant", admin, "User", "alice", "Service.View", inactive),
                TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.Success, added.Code);

            var filtered = Assert.IsType<AgentResult.ListServices>(await dispatcher.DispatchAsync(
                new AgentRequest.ListServices("real-list-filtered", alice),
                TestContext.Current.CancellationToken));
            Assert.Equal(inactive, Assert.Single(filtered.Services!).Id);

            var byAlias = Assert.IsType<AgentResult.InspectService>(await dispatcher.DispatchAsync(
                new AgentRequest.InspectService("real-alias", alice, alias),
                TestContext.Current.CancellationToken));
            Assert.Equal(AgentResultCode.Success, byAlias.Code);
            Assert.Equal(inactive, byAlias.Service?.Id);

            foreach (var target in new[] { lookalike, privileged, privilegedAlias,
                "systemd-journald.service", prefix + "-worker@.service", "../bad.service" })
            {
                var response = Assert.IsType<AgentResult.InspectService>(await dispatcher.DispatchAsync(
                    new AgentRequest.InspectService("real-deny", alice, target),
                    TestContext.Current.CancellationToken));
                Assert.NotEqual(AgentResultCode.Success, response.Code);
                Assert.Null(response.Service);
            }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(
                new AgentRequest.ListServices("real-cancel", admin), canceled.Token));

            FailAfterFixtureReadTransport? failingTransport = null;
            var failingInventory = new SystemdServiceInventory(
                token => new SystemdServiceEnumerator(async connectionToken =>
                {
                    failingTransport = new FailAfterFixtureReadTransport(
                        await SystemdDbusTransport.ConnectAsync(TimeSpan.FromSeconds(30), connectionToken),
                        inactive);
                    return failingTransport;
                }, TimeProvider.System).EnumerateAsync(token),
                (serviceId, token) => new SystemdServiceInspector().InspectAsync(serviceId, token));
            var failingDispatcher = new AgentDispatcher(
                new AgentConfiguration(2000, 2000, "sudo", 27, TimeSpan.FromMinutes(15)),
                identities, sessions, store, failingInventory, TimeProvider.System);
            var failedRead = Assert.IsType<AgentResult.ListServices>(await failingDispatcher.DispatchAsync(
                new AgentRequest.ListServices("real-systemd-failure", admin),
                TestContext.Current.CancellationToken));
            Assert.True(failingTransport?.FixtureRead);
            Assert.Equal(AgentResultCode.Unavailable, failedRead.Code);
            Assert.Null(failedRead.Services);
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT actor_name, actor_uid, operation, service, outcome FROM audit WHERE correlation_id = 'real-systemd-failure'";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("admin", reader.GetString(0));
                Assert.Equal(1001L, reader.GetInt64(1));
                Assert.Equal("ListServices", reader.GetString(2));
                Assert.True(reader.IsDBNull(3));
                Assert.Equal("Unavailable", reader.GetString(4));
            }

            Assert.Equal(unitBytes, File.ReadAllBytes(unitPath));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    private sealed class FailAfterFixtureReadTransport(
        ISystemdDbusTransport inner, string fixtureId) : ISystemdDbusTransport
    {
        public bool FixtureRead { get; private set; }

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
            inner.GetManagerVersionAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(CancellationToken cancellationToken) =>
            inner.ListUnitFilesAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(CancellationToken cancellationToken) =>
            inner.ListServiceUnitsAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
            IReadOnlyCollection<SystemServiceId> serviceIds, CancellationToken cancellationToken) =>
            inner.ListUnitsByNamesAsync(serviceIds, cancellationToken);

        public async Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
            SystemdUnitReference unit, CancellationToken cancellationToken)
        {
            var properties = await inner.ReadUnitPropertiesAsync(unit, cancellationToken);
            if (properties.Id == fixtureId)
            {
                FixtureRead = true;
                throw new SystemdDbusException(SystemdDbusFailureKind.Unavailable);
            }

            return properties;
        }

        public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            SystemdUnitReference unit, CancellationToken cancellationToken) =>
            inner.ReadEnvironmentPropertiesAsync(unit, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static async Task<string> LoginAsync(AgentDispatcher dispatcher, string username)
    {
        var result = Assert.IsType<AgentResult.Login>(await dispatcher.DispatchAsync(
            new AgentRequest.Login("real-login", username, "synthetic"),
            TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, result.Code);
        return Assert.IsType<string>(result.Session);
    }

    private sealed class FixtureIdentities : IAgentIdentityChecks
    {
        private static readonly LinuxAccount Admin = new("admin", 1001, 1001,
            [new LinuxGroup("sudo", 27)]);
        private static readonly LinuxAccount Alice = new("alice", 1002, 1002, []);

        public Task<AgentAuthentication> AuthenticateAsync(
            string username, string password, CancellationToken cancellationToken)
        {
            var account = Find(username);
            return Task.FromResult(account is null
                ? new AgentAuthentication(AgentResultStatus.Denied, null)
                : new AgentAuthentication(AgentResultStatus.Success, account));
        }

        public Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken) =>
            Task.FromResult(Find(name)?.Uid == uid);

        public Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(Find(username));

        public Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken) =>
            Task.FromResult(uid switch { 1001 => Admin, 1002 => Alice, _ => null });

        public Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken) =>
            Task.FromResult(groupName == "sudo" ? new LinuxGroup("sudo", 27) : null);

        private static LinuxAccount? Find(string name) => name switch
        {
            "admin" => Admin,
            "alice" => Alice,
            _ => null,
        };
    }
}
