using Serval.Domain.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class ServalPrivilegedUnitsTests
{
    [Theory]
    [InlineData("serval-agent.service")]
    [InlineData("serval-agent@.service")]
    [InlineData("serval-agent@tenant.service")]
    [InlineData(@"serval-agent@tenant\x2dblue.service")]
    public void ExcludesAgentServiceTemplateAndInstances(string name) =>
        Assert.True(ServalPrivilegedUnits.IsExcluded(new SystemServiceId(name)));

    [Theory]
    [InlineData("serval-web.service")]
    [InlineData("serval-agent-helper.service")]
    [InlineData("my-serval-agent.service")]
    [InlineData("Serval-agent.service")]
    [InlineData(@"\x73erval-agent.service")]
    [InlineData("worker@serval-agent.service")]
    [InlineData("ssh.service")]
    public void LeavesUnrelatedAndOtherProtectedServicesDiscoverable(string name) =>
        Assert.False(ServalPrivilegedUnits.IsExcluded(new SystemServiceId(name)));

    [Fact]
    public void ExcludesWholeIdentityWhenAnyValidatedAliasIsAnAgentUnit()
    {
        var ordinary = new SystemServiceId("ordinary.service");
        var agent = new SystemServiceId("serval-agent.service");

        Assert.True(ServalPrivilegedUnits.IsExcluded(ordinary, [ordinary, agent]));
        Assert.True(ServalPrivilegedUnits.IsExcluded(ordinary, [agent, ordinary, agent]));
        Assert.False(ServalPrivilegedUnits.IsExcluded(ordinary, [ordinary]));
    }
}
