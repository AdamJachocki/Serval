using Serval.Application.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdSourcePathTests
{
    [Theory]
    [InlineData("relative", EnvironmentUnsupportedReason.UnsafePath)]
    [InlineData("/a//b", EnvironmentUnsupportedReason.UnsafePath)]
    [InlineData("/a/./b", EnvironmentUnsupportedReason.UnsafePath)]
    [InlineData("/a/../b", EnvironmentUnsupportedReason.UnsafePath)]
    [InlineData("/a/*", EnvironmentUnsupportedReason.PathPattern)]
    [InlineData("/a/?", EnvironmentUnsupportedReason.PathPattern)]
    [InlineData("/a/[x]", EnvironmentUnsupportedReason.PathPattern)]
    [InlineData("/a/%n", EnvironmentUnsupportedReason.UnresolvedSpecifier)]
    [InlineData("/a\0b", EnvironmentUnsupportedReason.UnsafePath)]
    [InlineData("/a\nb", EnvironmentUnsupportedReason.UnsafePath)]
    public void RejectsAmbiguousPaths(string path, EnvironmentUnsupportedReason reason) =>
        Assert.Equal(reason, SystemdSourcePath.Validate(path));

    [Fact]
    public void EnforcesUtf8PathBoundary()
    {
        var exact = "/" + new string('é', (EnvironmentReadLimits.MaxPathBytes - 2) / 2) + "x";
        Assert.Null(SystemdSourcePath.Validate(exact));
        Assert.Equal(
            EnvironmentUnsupportedReason.UnsafePath,
            SystemdSourcePath.Validate(exact + "é"));
    }

    [Theory]
    [InlineData("/run/systemd/generator", true)]
    [InlineData("/run/systemd/generator/a.service", true)]
    [InlineData("/run/systemd/generator.early/a.service", true)]
    [InlineData("/run/systemd/generator.late/a.service", true)]
    [InlineData("/run/systemd/generator-other/a.service", false)]
    [InlineData("/run/systemd/system/a.service", false)]
    public void GeneratedClassificationUsesComponentBoundaries(string path, bool expected) =>
        Assert.Equal(expected, SystemdSourcePath.IsGenerated(path));
}
