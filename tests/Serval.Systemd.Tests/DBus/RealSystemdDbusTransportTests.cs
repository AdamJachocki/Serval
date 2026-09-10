using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests.DBus;

public sealed class RealSystemdDbusTransportTests
{
    public static bool IsEnabled =>
        OperatingSystem.IsLinux() &&
        string.Equals(
            Environment.GetEnvironmentVariable("SERVAL_REAL_SYSTEMD_TESTS"),
            "1",
            StringComparison.Ordinal);

    [Fact(
        Skip = "Requires a real Linux system manager and SERVAL_REAL_SYSTEMD_TESTS=1.",
        SkipUnless = nameof(IsEnabled))]
    public async Task SelectedContractMatchesRealSystemManager()
    {
        await using var transport = await SystemdDbusTransport.ConnectAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        var version = await transport.GetManagerVersionAsync(
            TestContext.Current.CancellationToken);
        var expectedMajorVersion = Environment.GetEnvironmentVariable(
            "SERVAL_EXPECTED_SYSTEMD_MAJOR");
        if (!string.IsNullOrEmpty(expectedMajorVersion))
        {
            var actualMajorVersion = new string(
                version.TakeWhile(char.IsAsciiDigit).ToArray());
            Assert.Equal(expectedMajorVersion, actualMajorVersion);
        }

        var unitFiles = await transport.ListUnitFilesAsync(
            TestContext.Current.CancellationToken);
        var loadedUnits = await transport.ListServiceUnitsAsync(
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(version);
        Assert.NotEmpty(unitFiles);
        var loadedUnit = Assert.Single(loadedUnits.Take(1));

        var namedUnits = await transport.ListUnitsByNamesAsync(
            [new SystemServiceId(loadedUnit.Name)],
            TestContext.Current.CancellationToken);
        var namedUnit = Assert.Single(namedUnits);
        var properties = await transport.ReadUnitPropertiesAsync(
            namedUnit.Unit,
            TestContext.Current.CancellationToken);

        Assert.Equal(loadedUnit.Name, namedUnit.Name);
        Assert.Equal(namedUnit.Name, properties.Id);
        Assert.Contains(namedUnit.Name, properties.Names);
        Assert.NotEmpty(properties.LoadState);
        Assert.NotEmpty(properties.ActiveState);
        Assert.NotEmpty(properties.SubState);
    }
}
