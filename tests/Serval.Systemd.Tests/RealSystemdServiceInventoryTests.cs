using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdServiceInventoryTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and disposable inventory fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task ApplicationInventoryComposesDiscoveryInspectionAndPolicyMetadata()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        var instanceId = prefix["serval-enumeration-test-".Length..];
        var privilegedName = "serval-agent@" + instanceId + ".service";
        var privilegedAliasName = "serval-exclusion-alias@" + instanceId + ".service";
        var lookalikeName = "serval-agent-helper@" + instanceId + ".service";
        string[] files = [prefix + "-inactive.service", privilegedName, lookalikeName];
        var before = files.ToDictionary(
            name => name,
            name => File.ReadAllBytes("/run/systemd/system/" + name),
            StringComparer.Ordinal);
        var inventory = new SystemdServiceInventory();

        var services = await inventory.ListAsync(TestContext.Current.CancellationToken);

        var ids = services.Select(service => service.Id.Value).ToArray();
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.True(Assert.Single(services, service =>
            service.Id.Value == "systemd-journald.service").IsProtected);
        var inactive = Assert.Single(services, service =>
            service.Id.Value == prefix + "-inactive.service");
        Assert.Equal("inactive", inactive.ActiveState.Value);
        Assert.Equal("dead", inactive.SubState.Value);
        Assert.False(inactive.IsProtected);
        Assert.DoesNotContain(privilegedName, ids);
        Assert.DoesNotContain(privilegedAliasName, ids);
        Assert.Contains(lookalikeName, ids);

        var alias = Assert.IsType<ServiceInspectionResult.Found>(await inventory.InspectAsync(
            new SystemServiceId(prefix + "-alias.service"), TestContext.Current.CancellationToken)).Service;
        Assert.Equal(prefix + "-inactive.service", alias.Id.Value);
        Assert.Equal(inactive, alias);
        Assert.True(Assert.IsType<ServiceInspectionResult.Found>(await inventory.InspectAsync(
            new SystemServiceId("systemd-journald.service"),
            TestContext.Current.CancellationToken)).Service.IsProtected);
        foreach (var name in new[] { privilegedName, privilegedAliasName })
        {
            var id = new SystemServiceId(name);
            Assert.Equal(id, Assert.IsType<ServiceInspectionResult.NotFound>(
                await inventory.InspectAsync(id, TestContext.Current.CancellationToken)).ServiceId);
        }

        foreach (var file in files)
        {
            Assert.Equal(before[file], File.ReadAllBytes("/run/systemd/system/" + file));
        }
    }
}
