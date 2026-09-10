using Serval.Domain.Services;
using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdServiceInspectionTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and disposable inventory fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task InspectsCanonicalAliasStatesAndInstancesWithoutMutatingFixtures()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        string[] files = [prefix + "-inactive.service", prefix + "-worker@.service", prefix + "-failed.service"];
        var before = files.ToDictionary(name => name,
            name => File.ReadAllBytes("/run/systemd/system/" + name), StringComparer.Ordinal);
        var inspector = new SystemdServiceInspector();
        async Task<SystemdServiceInspectionResult.Found> Inspect(string suffix) =>
            Assert.IsType<SystemdServiceInspectionResult.Found>(await inspector.InspectAsync(
                new SystemServiceId(prefix + suffix), TestContext.Current.CancellationToken));

        var canonical = await Inspect("-inactive.service");
        var alias = await Inspect("-alias.service");
        Assert.Equal(canonical.Service, alias.Service);
        Assert.Equal(canonical.Names, alias.Names);
        Assert.Equal(prefix + "-inactive.service", alias.Service.Id.Value);
        Assert.Equal("inactive", canonical.Service.ActiveState.Value);
        Assert.Equal("dead", canonical.Service.SubState.Value);
        Assert.Equal("Serval enumeration disposable inactive fixture", canonical.Service.Description);
        Assert.Equal("active", (await Inspect("-worker@loaded.service")).Service.ActiveState.Value);
        Assert.Equal("inactive", (await Inspect("-worker@installed.service")).Service.ActiveState.Value);
        Assert.Equal("active", (await Inspect("-transient.service")).Service.ActiveState.Value);
        Assert.Equal("failed", (await Inspect("-failed.service")).Service.ActiveState.Value);
        Assert.Equal("masked", (await Inspect("-masked.service")).Service.LoadState.Value);
        var instanceAlias = await Inspect("-helper@installed.service");
        var installed = await Inspect("-worker@installed.service");
        Assert.Equal(installed.Service, instanceAlias.Service);
        Assert.Contains(instanceAlias.Names, name => name.Value == prefix + "-helper@installed.service");
        Assert.Equal(prefix + "-worker@installed.service", instanceAlias.Service.Id.Value);
        Assert.NotEqual((await Inspect("-worker@loaded.service")).Service.Id, instanceAlias.Service.Id);
        var missingId = new SystemServiceId(prefix + "-nonexistent.service");
        Assert.Equal(missingId, Assert.IsType<SystemdServiceInspectionResult.NotFound>(
            await inspector.InspectAsync(missingId, TestContext.Current.CancellationToken)).ServiceId);
        await Assert.ThrowsAsync<ArgumentException>(() => inspector.InspectAsync(
            new SystemServiceId(prefix + "-worker@.service"), TestContext.Current.CancellationToken));
        foreach (var file in files)
        {
            Assert.Equal(before[file], File.ReadAllBytes("/run/systemd/system/" + file));
        }
        Assert.Equal("/dev/null", new FileInfo("/run/systemd/system/" + prefix + "-masked.service").LinkTarget);
        Assert.Equal(prefix + "-inactive.service", new FileInfo("/run/systemd/system/" + prefix + "-alias.service").LinkTarget);
    }
}
