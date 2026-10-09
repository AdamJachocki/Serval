using System.Text.Json;
using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class AgentConfigurationTests
{
    public static bool IsRootLinux => OperatingSystem.IsLinux() &&
        string.Equals(Environment.UserName, "root", StringComparison.Ordinal);

    [Fact]
    public void ValidConfigurationUsesDefaultIdleInterval()
    {
        var configuration = Parse("""{"webUid":1001,"webGid":1001,"adminGroup":"sudo","adminGid":27}""");
        Assert.Equal((uint)1001, configuration.WebUid);
        Assert.Equal((uint)27, configuration.AdminGid);
        Assert.Equal(TimeSpan.FromMinutes(15), configuration.IdleTimeout);
    }

    [Fact]
    public void ExplicitIdleIntervalIsApplied()
    {
        var configuration = Parse("""{"webUid":1001,"webGid":1001,"adminGroup":"wheel","adminGid":10,"idleMinutes":20}""");
        Assert.Equal(TimeSpan.FromMinutes(20), configuration.IdleTimeout);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"webUid\":0,\"webGid\":1001,\"adminGroup\":\"sudo\",\"adminGid\":27}")]
    [InlineData("{\"webUid\":1001,\"webGid\":1001,\"adminGroup\":\"sudo\",\"adminGid\":27,\"idleMinutes\":0}")]
    [InlineData("{\"webUid\":1001,\"webGid\":1001,\"adminGroup\":\"sudo\",\"adminGid\":27,\"idleMinutes\":121}")]
    [InlineData("{\"webUid\":1001,\"webGid\":1001,\"adminGroup\":\"sudo\",\"adminGid\":27,\"socketPath\":\"/tmp/agent.sock\"}")]
    [InlineData("{\"webUid\":1001,\"webUid\":1002,\"webGid\":1001,\"adminGroup\":\"sudo\",\"adminGid\":27}")]
    [InlineData("{\"webUid\":1001,\"webGid\":1001,\"adminGroup\":\"../sudo\",\"adminGid\":27}")]
    public void InvalidConfigurationFailsClosed(string json)
    {
        Assert.Throws<InvalidOperationException>(() => Parse(json));
    }

    private static AgentConfiguration Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return AgentConfiguration.Parse(document.RootElement);
    }

    [Fact(Skip = "Requires root-owned Linux path fixtures.", SkipUnless = nameof(IsRootLinux))]
    public void RootOwnedDeploymentPathsRejectUnsafeModesAndSymlinks()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"serval-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "agent.json");
        var policy = Path.Combine(root, "policy.db");
        var pam = Path.Combine(root, "serval-pam");
        var runtime = Path.Combine(root, "runtime");
        try
        {
            File.WriteAllText(config,
                """{"webUid":1001,"webGid":1001,"adminGroup":"sudo","adminGid":27}""");
            File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Equal((uint)1001, AgentConfiguration.LoadFromPaths(root, config).WebUid);

            File.SetUnixFileMode(config, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupWrite);
            Assert.Throws<InvalidOperationException>(() => AgentConfiguration.LoadFromPaths(root, config));
            File.Delete(config);
            File.WriteAllText(Path.Combine(root, "real.json"), "{}");
            File.CreateSymbolicLink(config, Path.Combine(root, "real.json"));
            Assert.Throws<InvalidOperationException>(() => AgentConfiguration.LoadFromPaths(root, config));
            File.Delete(config);

            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            AgentConfiguration.ValidatePolicyLocation(root, policy);
            File.WriteAllText(policy, string.Empty);
            File.SetUnixFileMode(policy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            AgentConfiguration.ValidatePolicyLocation(root, policy);
            File.SetUnixFileMode(policy, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.OtherRead);
            Assert.Throws<InvalidOperationException>(() =>
                AgentConfiguration.ValidatePolicyLocation(root, policy));
            File.Delete(policy);

            File.WriteAllText(pam, "auth required pam_permit.so\n");
            File.SetUnixFileMode(pam, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            AgentConfiguration.ValidatePamService(pam);
            File.SetUnixFileMode(pam, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.OtherWrite);
            Assert.Throws<InvalidOperationException>(() => AgentConfiguration.ValidatePamService(pam));

            Directory.CreateDirectory(runtime);
            File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
            var configuration = Parse(
                """{"webUid":1001,"webGid":1001,"adminGroup":"sudo","adminGid":27}""")
                with
            { WebGid = 0 };
            configuration.ValidateRuntimeDirectory(runtime);
            File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute);
            Assert.Throws<InvalidOperationException>(() => configuration.ValidateRuntimeDirectory(runtime));
        }
        finally
        {
            foreach (var file in new[] { config, policy, pam, Path.Combine(root, "real.json") })
            {
                if (File.Exists(file) || new FileInfo(file).LinkTarget is not null)
                {
                    File.Delete(file);
                }
            }

            if (Directory.Exists(runtime))
            {
                Directory.Delete(runtime, recursive: false);
            }

            Directory.Delete(root, recursive: false);
        }
    }

    [Fact]
    public async Task StartupRequiresExistingMatchingWebAndAdminIdentities()
    {
        var configuration = Parse("""{"webUid":1001,"webGid":1001,"adminGroup":"sudo","adminGid":27}""");
        var identities = new DeploymentIdentities
        {
            Web = new LinuxAccount("serval-web", 1001, 1001,
                [new LinuxGroup("serval-web", 1001)], "/nonexistent", "/usr/sbin/nologin", true),
            Admin = new LinuxGroup("sudo", 27),
        };
        await AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
            TestContext.Current.CancellationToken);

        identities.Web = identities.Web with { Uid = 1002 };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                TestContext.Current.CancellationToken));
        identities.Web = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                TestContext.Current.CancellationToken));
        var validWeb = new LinuxAccount("serval-web", 1001, 1001,
            [new LinuxGroup("serval-web", 1001)], "/nonexistent", "/usr/sbin/nologin", true);
        identities.Web = validWeb with
        {
            Groups = [new LinuxGroup("serval-web", 1001), new LinuxGroup("docker", 999)],
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                TestContext.Current.CancellationToken));
        identities.Web = validWeb with { Groups = [new LinuxGroup("other", 1001)] };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                TestContext.Current.CancellationToken));
        foreach (var unsafeWeb in new[]
        {
            validWeb with { Shell = "/bin/bash" },
            validWeb with { Home = "/home/serval-web" },
            validWeb with { PasswordLocked = false },
        })
        {
            identities.Web = unsafeWeb;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                    TestContext.Current.CancellationToken));
        }

        identities.Web = validWeb;
        identities.Admin = new LinuxGroup("sudo", 28);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities,
                TestContext.Current.CancellationToken));
    }

    private sealed class DeploymentIdentities : IAgentIdentityChecks
    {
        public LinuxAccount? Web { get; set; }

        public LinuxGroup? Admin { get; set; }

        public Task<AgentAuthentication> AuthenticateAsync(
            string username, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken) =>
            Task.FromResult(Web);

        public Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken) =>
            Task.FromResult(Admin);
    }
}
