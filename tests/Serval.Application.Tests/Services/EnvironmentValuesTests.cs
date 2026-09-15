using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Application.Tests.Services;

public sealed class EnvironmentValuesTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    private static readonly string[] MetadataProperties = ["Name", "WinningSourceId"];

    [Fact]
    public void OrdinaryPresentationAlwaysMasksSecrets()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var values = new EnvironmentValues([new("SYNTHETIC", marker)]);
        var outputs = new[]
        {
            values.ToString(), $"{values}", string.Format(CultureInfo.InvariantCulture, "{0}", values),
            string.Join(",", new[] { values }), JsonSerializer.Serialize(values),
            JsonSerializer.Serialize(new { Nested = values }),
            JsonSerializer.Serialize(new object[] { values }),
            JsonSerializer.Serialize<object>(values),
            JsonSerializer.Serialize(values, WebOptions),
            JsonSerializer.Serialize(new EnvironmentVariableMetadata("SYNTHETIC", 0)),
        };
        Assert.True(outputs.All(output => !output.Contains(marker, StringComparison.Ordinal)), "presentation");
        Assert.True(outputs.Take(4).All(output => output == "[REDACTED]"), "constant-formatting");
        Assert.True(JsonSerializer.Serialize(values) == "\"[REDACTED]\"", "constant-json");
        Assert.True(typeof(EnvironmentValues).GetCustomAttribute<DebuggerDisplayAttribute>()?.Value == "[REDACTED]", "debugger-display");
        var field = typeof(EnvironmentValues).GetField("values", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(field.GetCustomAttribute<DebuggerBrowsableAttribute>()?.State == DebuggerBrowsableState.Never, "debugger-buffer");
    }

    [Fact]
    public void PublicSurfaceCannotRevealOrConstructValues()
    {
        var type = typeof(EnvironmentValues);
        Assert.True(type.GetConstructors().Length == 0, "internal-construction");
        Assert.True(type.GetProperties().Length == 0 && type.GetFields().Length == 0, "no-secret-properties");
        Assert.True(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .All(method => method.Name is "Dispose" or "ToString"), "explicit-internal-access");
        Assert.True(typeof(EnvironmentVariableMetadata).GetProperties().Select(property => property.Name)
            .Order().SequenceEqual(MetadataProperties), "metadata-only");
    }

    [Fact]
    public void EmptyValueRemainsPresentAndUsesSameMask()
    {
        using var empty = new EnvironmentValues([new("EMPTY", "")]);
        using var absent = new EnvironmentValues([]);
        Assert.True(empty.Reveal("EMPTY").IsEmpty, "empty-present");
        Assert.True(empty.HasExactly(["EMPTY"]) && !empty.HasExactly([]), "empty-metadata");
        Assert.True(empty.ToString() == absent.ToString() && JsonSerializer.Serialize(empty) == JsonSerializer.Serialize(absent), "empty-mask");
        var error = Record.Exception(() => absent.Reveal("EMPTY"));
        Assert.True(error is KeyNotFoundException, "absent-distinct");
    }

    [Fact]
    public void ReplacementAndDisposalClearOwnedBuffersOnly()
    {
        var marker = Guid.NewGuid().ToString("N");
        var input = marker.ToCharArray();
        using var values = new EnvironmentValues([]);
        values.Set("SYNTHETIC", input);
        var previous = values.Reveal("SYNTHETIC");
        Assert.True(previous.SequenceEqual(input), "input-copied");
        values.Set("SYNTHETIC", previous);
        Assert.True(IsCleared(previous), "replaced-buffer-cleared");
        var current = values.Reveal("SYNTHETIC");
        Assert.True(current.SequenceEqual(input), "replacement-copied-before-clear");
        values.Dispose();
        values.Dispose();
        Assert.True(IsCleared(current), "disposed-buffer-cleared");
        Assert.True(input.AsSpan().SequenceEqual(marker.AsSpan()), "caller-retains-input");
        CheckError(() => values.Reveal(marker), marker, typeof(ObjectDisposedException), "disposed-read");
        CheckError(() => values.Set("SYNTHETIC", input), marker, typeof(ObjectDisposedException), "disposed-write");
        CheckError(() => values.HasExactly([]), marker, typeof(ObjectDisposedException), "disposed-validation");
        Assert.True(values.ToString() == "[REDACTED]" && JsonSerializer.Serialize(values) == "\"[REDACTED]\"", "disposed-mask");
    }

    [Fact]
    public void SuccessFreezesAndOwnsValues()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var values = new EnvironmentValues([new("SYNTHETIC", marker)]);
        var borrowed = values.Reveal("SYNTHETIC");
        using var result = new ServiceEnvironmentReadResult.Success(new SystemServiceId("example.service"),
            [new(0, EnvironmentSourceKind.ManagerEnvironment)], [new("SYNTHETIC", 0)], values);
        CheckError(() => values.Set("SYNTHETIC", marker), marker, typeof(InvalidOperationException), "published-immutable");
        Assert.True(!JsonSerializer.Serialize(result).Contains(marker, StringComparison.Ordinal), "success-json");
        result.Dispose();
        Assert.True(IsCleared(borrowed), "success-disposal");
    }

    [Fact]
    public void CancellationAndFailureScopesClearCandidateBuffers()
    {
        using var cancellation = new CancellationTokenSource();
        using var values = new EnvironmentValues([new("SYNTHETIC", Guid.NewGuid().ToString("N"))]);
        var borrowed = values.Reveal("SYNTHETIC");
        cancellation.Cancel();
        var error = Record.Exception(() =>
        {
            using (values)
                cancellation.Token.ThrowIfCancellationRequested();
        });
        Assert.True(error is OperationCanceledException && IsCleared(borrowed), "canceled-scope-cleared");
        using var failedValues = new EnvironmentValues([new("SYNTHETIC", Guid.NewGuid().ToString("N"))]);
        var failedBorrow = failedValues.Reveal("SYNTHETIC");
        error = Record.Exception(() =>
        {
            using (failedValues)
                _ = new ServiceEnvironmentReadResult.Success(new SystemServiceId("example.service"), [], [], failedValues);
        });
        Assert.True(error is ArgumentException && IsCleared(failedBorrow), "failed-publication-cleared");
    }

    [Fact]
    public void InputErrorsAndDeserializationNeverRetainSecretDiagnostics()
    {
        var marker = Guid.NewGuid().ToString("N");
        CheckError(() => new EnvironmentValues([new("BAD=" + marker, marker)]).Dispose(), marker, typeof(ArgumentException), "invalid-name");
        CheckError(() => new EnvironmentValues([new("SYNTHETIC", marker), new("SYNTHETIC", marker)]).Dispose(), marker, typeof(ArgumentException), "duplicate");
        CheckError(() => new EnvironmentValues([new("SYNTHETIC", null!)]).Dispose(), marker, typeof(ArgumentException), "null-value");
        CheckError(() => new EnvironmentValues(ThrowingInput(marker, false)).Dispose(), marker, typeof(ArgumentException), "enumerator-failure");
        CheckError(() => new EnvironmentValues(ThrowingInput(marker, true)).Dispose(), marker, typeof(OperationCanceledException), "enumerator-cancellation");
        CheckError(() => JsonSerializer.Deserialize<EnvironmentValues>(JsonSerializer.Serialize(marker)), marker, typeof(JsonException), "deserialize");
    }

    private static IEnumerable<KeyValuePair<string, string>> ThrowingInput(string marker, bool cancel)
    {
        yield return new("SYNTHETIC", marker);
        if (cancel)
            throw new OperationCanceledException(marker, new InvalidOperationException(marker));
        throw new InvalidOperationException(marker, new InvalidOperationException(marker));
    }

    private static void CheckError(Action action, string marker, Type expected, string scenario)
    {
        var error = Record.Exception(action);
        Assert.True(error?.GetType() == expected && error.InnerException is null &&
            !error.ToString().Contains(marker, StringComparison.Ordinal), scenario);
    }

    private static bool IsCleared(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (character != '\0')
                return false;
        return true;
    }
}
