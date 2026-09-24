using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdEnvironmentSourceReadResultTests
{
    [Fact]
    public void SuccessOwnsOrderedSourcesAndRedactsSerialization()
    {
        using var manager = Assert.IsType<LoadedEnvironmentResult.Success>(
            LoadedEnvironmentDecoder.Decode(["A=value"], TestContext.Current.CancellationToken));
        var bytes = new ClearableBytes();
        bytes.Append([1, 2, 3]);
        using var result = new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("a.service"),
            manager,
            [new SystemdEnvironmentFileSource(1, false, false, bytes),
             new SystemdEnvironmentFileSource(2, true, true, null)]);

        Assert.Equal([1, 2], result.FileSources.Select(source => source.SourceId));
        Assert.Equal(3, result.FileSources[0].Reveal().Length);
        Assert.True(result.FileSources[1].IsMissing);
        Assert.Equal("[REDACTED]", result.ToString());
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("1,2,3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonConsecutiveAndInvalidMissingSources()
    {
        using var manager = Assert.IsType<LoadedEnvironmentResult.Success>(
            LoadedEnvironmentDecoder.Decode([], TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => new SystemdEnvironmentSourceReadResult.Success(
            new SystemServiceId("a.service"),
            manager,
            [new SystemdEnvironmentFileSource(2, true, true, null)]));
        Assert.Throws<ArgumentException>(() => new SystemdEnvironmentFileSource(1, false, true, null));
    }

    [Fact]
    public void DisposalIsIdempotentAndClearsOwnedBytes()
    {
        var cleared = new List<byte[]>();
        var bytes = new ClearableBytes(clearedBufferObserver: cleared.Add);
        bytes.Append([10, 11]);
        bytes.Dispose();
        bytes.Dispose();

        Assert.NotEmpty(cleared);
        Assert.All(cleared, buffer => Assert.All(buffer, value => Assert.Equal(0, value)));
        Assert.Throws<ObjectDisposedException>(() => bytes.Reveal());
    }

    [Fact]
    public void FailureCarriesOnlyClosedSafeMetadata()
    {
        var failure = new SystemdEnvironmentSourceReadResult.Failure(
            EnvironmentReadFailureCode.UnsupportedConfiguration,
            EnvironmentUnsupportedReason.UnsafePath,
            3);
        Assert.Equal(EnvironmentReadFailureCode.UnsupportedConfiguration, failure.Code);
        Assert.Equal(EnvironmentUnsupportedReason.UnsafePath, failure.Reason);
        Assert.Equal(3, failure.SourceId);
        Assert.DoesNotContain("/", JsonSerializer.Serialize(failure), StringComparison.Ordinal);
    }
}
