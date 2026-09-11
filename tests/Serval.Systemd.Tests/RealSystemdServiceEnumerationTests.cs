using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdServiceEnumerationTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and the disposable enumeration fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task SnapshotIncludesInactiveAliasesInstancesAndTransientWithoutMutatingFixtures()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        string[] files = [prefix + "-inactive.service", prefix + "-worker@.service", prefix + "-worker@installed.service"];
        var before = files.ToDictionary(name => name,
            name => File.ReadAllBytes("/run/systemd/system/" + name), StringComparer.Ordinal);
        var snapshot = await new SystemdServiceEnumerator().EnumerateAsync(TestContext.Current.CancellationToken);
        Assert.True(Assert.Single(snapshot.Services, item => item.Service.Id.Value == "systemd-journald.service").IsProtected);
        var ids = snapshot.Services.Select(item => item.Service.Id.Value).ToArray();
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        var inactive = Assert.Single(snapshot.Services, item => item.Service.Id.Value == prefix + "-inactive.service");
        Assert.False(inactive.IsProtected);
        Assert.Equal("inactive", inactive.Service.ActiveState.Value);
        Assert.Equal("dead", inactive.Service.SubState.Value);
        Assert.Equal("Serval enumeration disposable inactive fixture", inactive.Service.Description);
        Assert.Contains(inactive.Names, name => name.Value == prefix + "-alias.service");
        Assert.DoesNotContain(prefix + "-alias.service", ids);
        Assert.DoesNotContain(prefix + "-worker@.service", ids);
        Assert.Contains(snapshot.Templates, name => name.Value == prefix + "-worker@.service");
        Assert.Contains(prefix + "-worker@installed.service", ids);
        Assert.Contains(prefix + "-worker@loaded.service", ids);
        Assert.Contains(prefix + "-transient.service", ids);
        var instance = Assert.Single(snapshot.Services, item => item.Service.Id.Value == prefix + "-worker@installed.service");
        Assert.Contains(instance.Names, name => name.Value == prefix + "-helper@installed.service");
        Assert.DoesNotContain(prefix + "-helper@installed.service", ids);
        Assert.Contains(snapshot.Templates, name => name.Value == prefix + "-helper@.service");
        Assert.DoesNotContain(prefix + "-helper@.service", ids);
        Assert.Equal(prefix + "-worker@installed.service",
            new FileInfo("/run/systemd/system/" + prefix + "-helper@installed.service").LinkTarget);
        Assert.Equal(prefix + "-worker@.service",
            new FileInfo("/run/systemd/system/" + prefix + "-helper@.service").LinkTarget);
        foreach (var file in files)
        {
            Assert.Equal(before[file], File.ReadAllBytes("/run/systemd/system/" + file));
        }
    }
}
