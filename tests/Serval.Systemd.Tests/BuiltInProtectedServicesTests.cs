using Serval.Domain.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class BuiltInProtectedServicesTests
{
    [Theory]
    [InlineData("serval-agent.service")]
    [InlineData("dbus.service")]
    [InlineData("dbus-broker.service")]
    [InlineData("systemd-journald.service")]
    [InlineData("systemd-logind.service")]
    [InlineData("systemd-udevd.service")]
    [InlineData("systemd-networkd.service")]
    [InlineData("systemd-resolved.service")]
    [InlineData("NetworkManager.service")]
    [InlineData("networking.service")]
    [InlineData("ssh.service")]
    [InlineData("sshd.service")]
    [InlineData("polkit.service")]
    [InlineData("systemd-user-sessions.service")]
    public void ProtectsCriticalExactNames(string name) =>
        Assert.True(BuiltInProtectedServices.IsProtected(new SystemServiceId(name)));

    [Theory]
    [InlineData("serval-agent")]
    [InlineData("systemd-journald")]
    [InlineData("ssh")]
    [InlineData("sshd")]
    [InlineData("user")]
    public void ProtectsTemplatesAndEveryInstanceWithoutDecoding(string family)
    {
        foreach (var instance in new[] { "", "1000", "tenant", @"tenant\x2dblue" })
        {
            Assert.True(BuiltInProtectedServices.IsProtected(new SystemServiceId($"{family}@{instance}.service")));
        }
    }

    [Theory]
    [InlineData("serval-agent-helper.service")]
    [InlineData("serval-web.service")]
    [InlineData("dbus-monitor.service")]
    [InlineData("systemd-journald-helper@tenant.service")]
    [InlineData("systemd-custom.service")]
    [InlineData("ssh-backup.service")]
    [InlineData("my-sshd.service")]
    [InlineData("sshd.service.backup.service")]
    [InlineData("worker@ssh.service")]
    [InlineData("user-helper@1000.service")]
    [InlineData("user.service")]
    [InlineData("networkmanager.service")]
    [InlineData("SSH.service")]
    [InlineData(@"\x73sh.service")]
    public void LeavesUnrelatedNamesUnprotected(string name) =>
        Assert.False(BuiltInProtectedServices.IsProtected(new SystemServiceId(name)));

    [Fact]
    public void IncludesCanonicalIdEvenWhenNamesAreEmptyAndIgnoresAliasOrder()
    {
        var critical = new SystemServiceId("serval-agent.service");
        var ordinary = new SystemServiceId("ordinary.service");
        Assert.True(BuiltInProtectedServices.IsProtected(critical, []));
        Assert.True(BuiltInProtectedServices.IsProtected(ordinary, [ordinary, critical]));
        Assert.True(BuiltInProtectedServices.IsProtected(ordinary, [critical, ordinary, critical]));
        Assert.False(BuiltInProtectedServices.IsProtected(ordinary, [ordinary]));
    }
}
