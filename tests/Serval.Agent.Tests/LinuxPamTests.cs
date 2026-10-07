using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class LinuxPamTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "Requires Linux-PAM and a disposable configuration directory.", SkipUnless = nameof(IsLinux))]
    public void PermittingAndDenyingPamStacksReturnOnlyTheOutcome()
    {
        var directory = Path.Combine(Path.GetTempPath(), "serval-pam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = Path.Combine(directory, AgentConfiguration.PamService);
            File.WriteAllText(service, "auth required pam_permit.so\naccount required pam_permit.so\n");
            Assert.True(LinuxPam.AuthenticateWithConfiguration("serval-test", "synthetic-password", directory));

            File.WriteAllText(service, "auth required pam_deny.so\naccount required pam_permit.so\n");
            Assert.False(LinuxPam.AuthenticateWithConfiguration("serval-test", "incorrect-password", directory));

            File.WriteAllText(service,
                "auth required pam_exec.so expose_authtok quiet /usr/bin/grep -qx synthetic-password\n" +
                "account required pam_permit.so\n");
            Assert.True(LinuxPam.AuthenticateWithConfiguration(
                "serval-test", "synthetic-password", directory));
            Assert.False(LinuxPam.AuthenticateWithConfiguration(
                "serval-test", "wrong-password", directory));

            File.WriteAllText(service, "auth required pam_permit.so\naccount required pam_deny.so\n");
            Assert.False(LinuxPam.AuthenticateWithConfiguration(
                "serval-test", "synthetic-password", directory));
        }
        finally
        {
            File.Delete(Path.Combine(directory, AgentConfiguration.PamService));
            Directory.Delete(directory);
        }
    }
}
