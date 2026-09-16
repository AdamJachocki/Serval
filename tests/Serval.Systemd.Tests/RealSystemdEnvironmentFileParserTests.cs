using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Serval.Application.Services;
using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdEnvironmentFileParserTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and disposable environment-file fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task MatchesManagerForCommonGrammarAndPreservesFixtureBytes()
    {
        var fixture = GetFixture();
        var sourcePath = fixture.Root + "/" + fixture.Prefix + "-environment-file.env";
        var unitPath = fixture.Root + "/" + fixture.Prefix + "-environment-file.service";
        var sourceBefore = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        var unitBefore = await File.ReadAllBytesAsync(unitPath, TestContext.Current.CancellationToken);
        Dictionary<string, byte[]>? processEnvironment = null;
        try
        {
            using var parsed = Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
                sourceBefore, 41, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken));
            processEnvironment = await ReadProcessEnvironmentAsync(fixture.Prefix + "-environment-file.service");
            Assert.True(parsed.Values.HasExactly(parsed.Variables.Select(variable => variable.Name)), "metadata-values");
            foreach (var variable in parsed.Variables)
            {
                Assert.True(processEnvironment.TryGetValue(variable.Name, out var managerValue), variable.Name);
                var expected = new byte[Encoding.UTF8.GetByteCount(parsed.Values.Reveal(variable.Name))];
                try
                {
                    _ = Encoding.UTF8.GetBytes(parsed.Values.Reveal(variable.Name), expected);
                    Assert.True(expected.AsSpan().SequenceEqual(managerValue), variable.Name);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(expected);
                }
            }

            var sourceAfter = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
            var unitAfter = await File.ReadAllBytesAsync(unitPath, TestContext.Current.CancellationToken);
            try
            {
                Assert.True(sourceBefore.AsSpan().SequenceEqual(sourceAfter), "source-unchanged");
                Assert.True(unitBefore.AsSpan().SequenceEqual(unitAfter), "unit-unchanged");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sourceAfter);
                CryptographicOperations.ZeroMemory(unitAfter);
            }
        }
        finally
        {
            if (processEnvironment is not null)
                Clear(processEnvironment);
            CryptographicOperations.ZeroMemory(sourceBefore);
            CryptographicOperations.ZeroMemory(unitBefore);
            await StopAsync(fixture.Prefix + "-environment-file.service");
        }
    }

    [Fact(Skip = "Requires real systemd and disposable environment-file fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task ConfirmsVersionDivergenceAndServalCompatibilityPolicy()
    {
        var fixture = GetFixture();
        var expectedMajor = int.Parse(Environment.GetEnvironmentVariable("SERVAL_EXPECTED_SYSTEMD_MAJOR")!,
            System.Globalization.CultureInfo.InvariantCulture);
        foreach (var ending in new[] { "lf", "cr", "crlf" })
        {
            var sourcePath = fixture.Root + "/" + fixture.Prefix + "-environment-divergence-" + ending + ".env";
            var unitName = fixture.Prefix + "-environment-divergence@" + ending + ".service";
            var before = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
            Dictionary<string, byte[]>? processEnvironment = null;
            try
            {
                var parsed = EnvironmentFileParser.Parse(
                    before, 42, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken);
                if (ending == "crlf")
                {
                    using var success = Assert.IsType<EnvironmentFileParseResult.Success>(parsed);
                    processEnvironment = await ReadProcessEnvironmentAsync(unitName);
                    Assert.True(processEnvironment.TryGetValue("AFTER", out var managerValue), "crlf-present");
                    var expected = new byte[Encoding.UTF8.GetByteCount(success.Values.Reveal("AFTER"))];
                    try
                    {
                        _ = Encoding.UTF8.GetBytes(success.Values.Reveal("AFTER"), expected);
                        Assert.True(expected.AsSpan().SequenceEqual(managerValue), "crlf-value");
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(expected);
                    }
                }
                else
                {
                    Assert.Equal(EnvironmentReadFailureCode.InvalidSource,
                        Assert.IsType<EnvironmentFileParseResult.Failure>(parsed).Code);
                    processEnvironment = await ReadProcessEnvironmentAsync(unitName);
                    Assert.Equal(expectedMajor >= 254, processEnvironment.ContainsKey("AFTER"));
                }

                var after = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
                try
                {
                    Assert.True(before.AsSpan().SequenceEqual(after), ending + "-source-unchanged");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(after);
                }
            }
            finally
            {
                if (processEnvironment is not null)
                    Clear(processEnvironment);
                CryptographicOperations.ZeroMemory(before);
                await StopAsync(unitName);
            }
        }
    }

    [Fact(Skip = "Requires real systemd and disposable environment-file fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task RejectsForbiddenUnicodeFixtureWithoutChangingIt()
    {
        var fixture = GetFixture();
        var sourcePath = fixture.Root + "/" + fixture.Prefix + "-environment-invalid.env";
        var before = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
        try
        {
            var result = Assert.IsType<EnvironmentFileParseResult.Failure>(EnvironmentFileParser.Parse(
                before, 43, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken));
            Assert.Equal(EnvironmentReadFailureCode.InvalidSource, result.Code);
            var after = await File.ReadAllBytesAsync(sourcePath, TestContext.Current.CancellationToken);
            try
            {
                Assert.True(before.AsSpan().SequenceEqual(after), "invalid-source-unchanged");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(after);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(before);
        }
    }

    private static (string Prefix, string Root) GetFixture()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        Assert.NotNull(Environment.GetEnvironmentVariable("SERVAL_EXPECTED_SYSTEMD_MAJOR"));
        return (prefix, "/run/systemd/system");
    }

    private static async Task<Dictionary<string, byte[]>> ReadProcessEnvironmentAsync(string unitName)
    {
        await RunAsync("/usr/bin/systemctl", ["start", unitName]);
        var pidText = await RunAsync("/usr/bin/systemctl", ["show", "--property=MainPID", "--value", unitName]);
        Assert.True(int.TryParse(pidText.Trim(), out var pid) && pid > 0, "fixture-main-pid");
        var raw = await File.ReadAllBytesAsync("/proc/" + pid + "/environ", TestContext.Current.CancellationToken);
        try
        {
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var start = 0;
            for (var index = 0; index <= raw.Length; index++)
            {
                if (index < raw.Length && raw[index] != 0)
                    continue;
                var entry = raw.AsSpan(start, index - start);
                var separator = entry.IndexOf((byte)'=');
                if (separator > 0)
                    result[Encoding.ASCII.GetString(entry[..separator])] = entry[(separator + 1)..].ToArray();
                start = index + 1;
            }

            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    private static async Task StopAsync(string unitName)
    {
        try
        {
            await RunAsync("/usr/bin/systemctl", ["stop", unitName]);
        }
        catch (InvalidOperationException)
        {
            // The harness cleanup is the final safety net when a fixture never started.
        }
    }

    private static async Task<string> RunAsync(string executable, string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            _ = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Environment-file fixture command failed.");
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

    private static void Clear(Dictionary<string, byte[]> environment)
    {
        foreach (var value in environment.Values)
            CryptographicOperations.ZeroMemory(value);
        environment.Clear();
    }
}
