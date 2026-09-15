using System.Diagnostics;
using System.Text.Json;
using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdLoadedEnvironmentTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and disposable enumeration fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task DecodesManagerQuotingResetsSpecifiersAndIgnoredInvalidAssignment()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        var name = prefix + "-environment@sample.service";
        var unit = "/run/systemd/system/" + name;
        var dropin = unit + ".d/10-environment.conf";
        var cancellationToken = TestContext.Current.CancellationToken;
        var beforeUnit = await File.ReadAllBytesAsync(unit, cancellationToken);
        var beforeDropin = await File.ReadAllBytesAsync(dropin, cancellationToken);
        var marker = await File.ReadAllTextAsync(unit + ".d/expected", cancellationToken);
        // Harness oracle only: production decoder has no process or D-Bus dependency.
        _ = await RunAsync("/usr/bin/systemctl", ["show", "--property=LoadState", name], cancellationToken);
        var escapedName = name.Replace("-", "_2d", StringComparison.Ordinal)
            .Replace("@", "_40", StringComparison.Ordinal).Replace(".", "_2e", StringComparison.Ordinal);
        var response = await RunAsync("/usr/bin/busctl", ["--json=short", "get-property", "org.freedesktop.systemd1",
            "/org/freedesktop/systemd1/unit/" + escapedName, "org.freedesktop.systemd1.Service", "Environment"], cancellationToken);
        string[] entries;
        try
        {
            using var json = JsonDocument.Parse(response);
            entries = json.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetString()!).ToArray();
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Invalid fixture property response.");
        }

        using var result = Assert.IsType<LoadedEnvironmentResult.Success>(LoadedEnvironmentDecoder.Decode(entries, cancellationToken));
        Assert.Equal(["EMPTY", "ESCAPED", "LITERAL", "SPECIFIER", "VALUE"], result.Variables.Select(item => item.Name));
        Assert.True(result.Values.Reveal("EMPTY").IsEmpty);
        Assert.True(result.Values.Reveal("VALUE").SequenceEqual(marker + " final"));
        Assert.True(result.Values.Reveal("ESCAPED").SequenceEqual(marker + "\n\t\\"));
        Assert.True(result.Values.Reveal("LITERAL").SequenceEqual(marker + " $HOME $(id) `id`"));
        Assert.True(result.Values.Reveal("SPECIFIER").SequenceEqual(name + "/sample/%"));
        Assert.All(result.Variables, variable => Assert.Equal(0, variable.WinningSourceId));
        Assert.False(JsonSerializer.Serialize(result).Contains(marker, StringComparison.Ordinal));
        var afterUnit = await File.ReadAllBytesAsync(unit, cancellationToken);
        var afterDropin = await File.ReadAllBytesAsync(dropin, cancellationToken);
        Assert.True(beforeUnit.SequenceEqual(afterUnit));
        Assert.True(beforeDropin.SequenceEqual(afterDropin));
    }

    private static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await error;
            Assert.True(process.ExitCode == 0, "Fixture property query failed.");
            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
