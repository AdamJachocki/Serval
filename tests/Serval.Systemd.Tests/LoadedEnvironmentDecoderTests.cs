using System.Text.Json;
using Serval.Application.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class LoadedEnvironmentDecoderTests
{
    [Fact]
    public void PreservesExactValuesAndOnlyWinningMetadata()
    {
        var secret = Guid.NewGuid().ToString();
        var literal = " \"'\\n $HOME ${USER} $(touch /tmp/unused) `id` %n %% =\n\t" + secret;
        using var result = Success(["Z=" + secret, "A=" + secret, "A=", "a=" + literal]);
        Assert.Equal(["A", "Z", "a"], result.Variables.Select(item => item.Name));
        Assert.All(result.Variables, item => Assert.Equal(0, item.WinningSourceId));
        Assert.Equal(EnvironmentSourceKind.ManagerEnvironment, result.Source.Kind);
        Assert.True(result.Values.Reveal("A").IsEmpty);
        Assert.True(result.Values.Reveal("Z").SequenceEqual(secret));
        Assert.True(result.Values.Reveal("a").SequenceEqual(literal));
        Assert.Equal(4, result.Assignments);
        Assert.False((result + JsonSerializer.Serialize(result) + JsonSerializer.Serialize(result.Values)).Contains(secret, StringComparison.Ordinal));
        result.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = result.Values.Reveal("A"); });
    }

    [Fact]
    public void EmptyArrayIsAnEmptyCandidate()
    {
        using var result = Success([]);
        Assert.Empty(result.Variables);
        Assert.Equal(0, result.SourceBytes);
        Assert.Equal(0, result.Assignments);
    }

    [Fact]
    public void RejectsMalformedInputWithoutExposingEarlierValues()
    {
        var secret = Guid.NewGuid().ToString();
        string[] invalid = ["", "=", "NAME", "1A=", "A-B=", " A=", "A =", "Ą=", "A=\0", "A=\ud800", "A=\udc00", null!];
        foreach (var entry in invalid)
        {
            var result = Assert.IsType<LoadedEnvironmentResult.Failure>(
                LoadedEnvironmentDecoder.Decode(["SAFE=" + secret, entry], TestContext.Current.CancellationToken));
            Assert.Equal(EnvironmentReadFailureCode.InvalidSource, result.Code);
            Assert.Equal(0, result.SourceId);
            Assert.False(JsonSerializer.Serialize(result).Contains(secret, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CountsUtf8BytesAtEntryBoundary()
    {
        var entry = "A=" + new string('é', (LoadedEnvironmentDecoder.MaxEntryBytes - 2) / 2);
        using var result = Success([entry]);
        Assert.Equal(LoadedEnvironmentDecoder.MaxEntryBytes, result.SourceBytes);
        Assert.True(result.Values.Reveal("A").SequenceEqual(entry.AsSpan(2)));
        Limit([entry + "x"]);
        Limit(["A=" + new string('x', LoadedEnvironmentDecoder.MaxEntryBytes)]);
        using var supplementary = Success(["A=" + char.ConvertFromUtf32(0x1F600)]);
        Assert.Equal(6, supplementary.SourceBytes);
    }

    [Fact]
    public void CountsOverriddenEntriesAtTotalSourceBoundary()
    {
        var entry = "A=" + new string('x', LoadedEnvironmentDecoder.MaxEntryBytes - 2);
        var entries = Enumerable.Repeat(entry, 16).ToArray();
        using var result = Success(entries);
        Assert.Equal(LoadedEnvironmentDecoder.MaxSourceBytes, result.SourceBytes);
        Assert.Single(result.Variables);
        // One byte above the aggregate limit, while every entry remains within its limit.
        entries[0] = entry[..^1];
        Limit([.. entries, "B="]);
    }

    [Fact]
    public void CountsAssignmentsBeforeDeduplication()
    {
        var entries = Enumerable.Repeat("A=", LoadedEnvironmentDecoder.MaxAssignments).ToArray();
        using var result = Success(entries);
        Assert.Equal(LoadedEnvironmentDecoder.MaxAssignments, result.Assignments);
        Assert.Single(result.Variables);
        Limit([.. entries, "A="]);
    }

    [Fact]
    public void CallerCancellationWinsEvenForEmptyOrInvalidInput()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        foreach (var entries in new string[][] { [], ["INVALID"] })
        {
            var exception = Assert.Throws<OperationCanceledException>(() => LoadedEnvironmentDecoder.Decode(entries, source.Token));
            Assert.Equal(source.Token, exception.CancellationToken);
            Assert.Null(exception.InnerException);
        }
    }

    private static LoadedEnvironmentResult.Success Success(string[] entries) =>
        Assert.IsType<LoadedEnvironmentResult.Success>(LoadedEnvironmentDecoder.Decode(entries, TestContext.Current.CancellationToken));

    private static void Limit(string[] entries) => Assert.Equal(EnvironmentReadFailureCode.LimitExceeded,
        Assert.IsType<LoadedEnvironmentResult.Failure>(LoadedEnvironmentDecoder.Decode(entries, TestContext.Current.CancellationToken)).Code);
}
