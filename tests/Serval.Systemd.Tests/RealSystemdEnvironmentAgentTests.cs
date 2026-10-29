using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serval.Agent;
using Serval.Application.Authorization;
using Serval.Application.Ipc;
using Serval.EnvironmentOracle;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed partial class RealSystemdEnvironmentSourceReaderTests
{
    [Theory(Skip = "Requires disposable real-systemd fixtures.", SkipUnless = nameof(IsEnabled))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentDisclosesOnlyManifestSelectedValuesWithoutSystemMutation(bool sourceFiles)
    {
        var prefix = RequiredPrefix();
        var canonical = prefix + (sourceFiles ? "-source@sample.service" : "-environment@sample.service");
        var target = sourceFiles ? prefix + "-source-alias@sample.service" : canonical;
        var identity = new EnvironmentIdentity();
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), identity, TimeProvider.System);
        var audit = new EnvironmentAudit();
        var dispatcher = new AgentDispatcher(new AgentConfiguration(2000, 2000, "sudo", 27, TimeSpan.FromMinutes(15)),
            identity, sessions, audit, new SystemdServiceInventory(), TimeProvider.System, new SystemdServiceEnvironmentReader());
        var login = Assert.IsType<AgentResult.Login>(await dispatcher.DispatchAsync(
            new AgentRequest.Login("env-login", "admin", "fixture"), TestContext.Current.CancellationToken));
        var session = login.Session!;
        await using var observation = await ProductObservation.CreateAsync(prefix, canonical, sourceFiles, TestContext.Current.CancellationToken);
        var metadata = Assert.IsType<AgentResult.ListEnvironment>(await dispatcher.DispatchAsync(
            new AgentRequest.ListEnvironment("env-list", session, target), TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Success, metadata.Code);
        Assert.Equal(canonical, metadata.Environment!.Service);
        var path = "/run/serval-environment-tests/" + prefix + (sourceFiles ? "/source.manifest" : "/environment.manifest");
        using var input = File.OpenRead(path);
        using var manifest = ExpectationManifest.Read(input);
        foreach (var entry in manifest.Entries)
        {
            using var result = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await dispatcher.DispatchAsync(
                new AgentRequest.RevealEnvironmentVariable("env-reveal", session, target, entry.Name), TestContext.Current.CancellationToken));
            Assert.Equal(entry.IsPresent ? AgentResultCode.Success : AgentResultCode.NotFound, result.Code);
            if (!entry.IsPresent) { Assert.Null(result.Variable); continue; }
            Assert.Equal(entry.Name, audit.Entries[^1].VariableName);
            Assert.Equal("Success", audit.Entries[^1].Outcome);
            using var wire = new MemoryStream();
            await AgentProtocol.WriteResultAsync(wire, result, TestContext.Current.CancellationToken);
            wire.Position = 0;
            using var received = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await AgentProtocol.ReadResultAsync(wire,
                new AgentRequest.RevealEnvironmentVariable("env-reveal", session, target, entry.Name), TestContext.Current.CancellationToken));
            using var http = new MemoryStream();
            await received.Variable!.WriteHttpAsync(http, TestContext.Current.CancellationToken);
            using var json = JsonDocument.Parse(http.ToArray());
            var bytes = Encoding.UTF8.GetBytes(json.RootElement.GetProperty("value").GetString()!);
            try
            {
                Assert.True(bytes.AsSpan().SequenceEqual(manifest.GetValueBytes(entry)), "single-value-manifest");
                var serializedMetadata = JsonSerializer.SerializeToUtf8Bytes(metadata);
                var serializedAudit = JsonSerializer.SerializeToUtf8Bytes(audit.Entries);
                if (bytes.Length > 8)
                {
                    Assert.False(serializedMetadata.AsSpan().IndexOf(bytes) >= 0, "metadata-leakage");
                    Assert.False(serializedAudit.AsSpan().IndexOf(bytes) >= 0, "audit-leakage");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                CryptographicOperations.ZeroMemory(wire.GetBuffer());
                CryptographicOperations.ZeroMemory(http.GetBuffer());
            }
        }
        foreach (var suffix in new[] { "-source-required-missing.service", "-source-unsupported.service", "-source-symlink.service" })
        {
            var failed = Assert.IsType<AgentResult.ListEnvironment>(await dispatcher.DispatchAsync(
                new AgentRequest.ListEnvironment("env-failure", session, prefix + suffix), TestContext.Current.CancellationToken));
            Assert.NotEqual(AgentResultCode.Success, failed.Code);
            Assert.Null(failed.Environment);
        }
        var instance = prefix["serval-enumeration-test-".Length..];
        foreach (var protectedTarget in new[] { "systemd-journald.service", "serval-agent@" + instance + ".service", "serval-exclusion-alias@" + instance + ".service" })
        {
            using var denied = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await dispatcher.DispatchAsync(
                new AgentRequest.RevealEnvironmentVariable("env-protected", session, protectedTarget, "CONFIG"), TestContext.Current.CancellationToken));
            Assert.NotEqual(AgentResultCode.Success, denied.Code);
            Assert.Null(denied.Variable);
        }
        await observation.AssertUnchangedAsync(TestContext.Current.CancellationToken);
    }

    private sealed class EnvironmentAudit : IAgentPolicyStore
    {
        public List<AuditEntry> Entries { get; } = [];
        public IReadOnlyList<StoredGrant> ListGrants() => [];
        public long AddGrant(StoredGrant grant, AuditEntry audit) => throw new NotSupportedException();
        public bool RemoveGrant(long id, AuditEntry audit) => throw new NotSupportedException();
        public void AppendAudit(AuditEntry audit) => Entries.Add(audit);
    }

    private sealed class EnvironmentIdentity : IAgentIdentityChecks
    {
        private static readonly LinuxAccount Admin = new("admin", 1001, 1001, [new LinuxGroup("sudo", 27)]);
        public Task<AgentAuthentication> AuthenticateAsync(string username, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new AgentAuthentication(AgentResultStatus.Success, Admin));
        public Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken) => Task.FromResult(name == Admin.Name && uid == Admin.Uid);
        public Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken) => Task.FromResult<LinuxAccount?>(username == Admin.Name ? Admin : null);
        public Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken) => Task.FromResult<LinuxAccount?>(uid == Admin.Uid ? Admin : null);
        public Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken) => Task.FromResult<LinuxGroup?>(null);
    }
}
