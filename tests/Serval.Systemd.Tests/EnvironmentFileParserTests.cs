using System.Text;
using System.Text.Json;
using Serval.Application.Services;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class EnvironmentFileParserTests
{
    [Fact]
    public void ParsesSystemdGrammarAndPreservesLiteralValues()
    {
        var marker = Guid.NewGuid().ToString("N");
        var source = "  # comment\n; another\r\nignored text\n" +
            " EMPTY =   \n" +
            "UNQUOTED=  alpha\\ beta  \n" +
            "SINGLE='one\n two'\n" +
            "DOUBLE=\"a\\$b\\qc\\\nd\"\n" +
            "JOINED=\"a\"'b'c\n" +
            $"LITERAL=$NAME ${{NAME}} $(command) `command` %n %% =={marker}\n" +
            "PERMITTED=zażółć 😀\n" +
            "DUPLICATE=first\nDUPLICATE=final";

        using var result = Success(source, 7);
        Assert.Equal(9, result.Assignments);
        Assert.Equal(Encoding.UTF8.GetByteCount(source), result.SourceBytes);
        Assert.Equal(result.Variables.Select(variable => variable.Name).Order(StringComparer.Ordinal),
            result.Variables.Select(variable => variable.Name));
        Assert.All(result.Variables, variable => Assert.Equal(7, variable.WinningSourceId));
        Equal(result, "EMPTY", "");
        Equal(result, "UNQUOTED", "alpha beta");
        Equal(result, "SINGLE", "one\n two");
        Equal(result, "DOUBLE", "a$b\\qcd");
        Equal(result, "JOINED", "abc");
        Equal(result, "LITERAL", "$NAME ${NAME} $(command) `command` %n %% ==" + marker);
        Equal(result, "PERMITTED", "zażółć 😀");
        Equal(result, "DUPLICATE", "final");
        Assert.False((result + JsonSerializer.Serialize<object>(result) + JsonSerializer.Serialize(result.Values))
            .Contains(marker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("# comment \\\nSAFE=value\n")]
    [InlineData("; comment \\\nSAFE=value\n")]
    [InlineData("# comment \\\rSAFE=value\r")]
    [InlineData("; comment \\\rSAFE=value\r")]
    public void RejectsVersionDivergentCommentEndings(string source)
    {
        var result = Failure(source, 11);
        Assert.Equal(EnvironmentReadFailureCode.InvalidSource, result.Code);
        Assert.Equal(11, result.SourceId);
    }

    [Theory]
    [InlineData("# comment \\\r\nSAFE=value\r\n")]
    [InlineData("; comment \\\r\nSAFE=value\r\n")]
    public void AcceptsStableCrLfCommentEnding(string source)
    {
        using var result = Success(source, 12);
        Equal(result, "SAFE", "value");
    }

    [Fact]
    public void RejectsMalformedSourceAsAWholeWithoutSecretDiagnostics()
    {
        var marker = Guid.NewGuid().ToString("N");
        var invalidText = new[]
        {
            "=value", "1NAME=value", "A-B=value", "Ą=value", "export NAME=value",
            "NAME='unterminated", "NAME=\"unterminated", "NAME=ok\nBAD NAME=value",
            "\uFEFFNAME=ok", "NA\uFEFFME=ok", "NAME=\uFEFFok", "NAME=ok\uFEFF",
            "NAME=ok\nNONCHAR=\uFDD0", "NAME=ok\nNONCHAR=\uFFFF",
            "NAME=ok\nNONCHAR=" + char.ConvertFromUtf32(0x1FFFE),
            "NAME=ok\nNONCHAR=" + char.ConvertFromUtf32(0x10FFFF),
        };

        foreach (var invalid in invalidText)
        {
            var result = Failure("SAFE=" + marker + "\n" + invalid, 3);
            Assert.Equal(EnvironmentReadFailureCode.InvalidSource, result.Code);
            Assert.False((result + JsonSerializer.Serialize(result)).Contains(marker, StringComparison.Ordinal));
        }

        foreach (var bytes in new byte[][]
        {
            [0xC3, 0x28], [0xE2, 0x82], [(byte)'A', (byte)'=', 0], [0xEF, 0xBB, 0xBF, (byte)'A', (byte)'=', (byte)'1'],
        })
        {
            var result = Assert.IsType<EnvironmentFileParseResult.Failure>(EnvironmentFileParser.Parse(
                bytes, 3, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken));
            Assert.Equal(EnvironmentReadFailureCode.InvalidSource, result.Code);
        }
    }

    [Fact]
    public void EnforcesInclusiveByteAndLogicalRecordLimits()
    {
        var exactSource = Enumerable.Repeat((byte)'\n', EnvironmentReadLimits.MaxSourceBytes).ToArray();
        using (var result = Success(exactSource, 1))
            Assert.Equal(EnvironmentReadLimits.MaxSourceBytes, result.SourceBytes);
        Limit([.. exactSource, (byte)'\n']);

        var exactRecord = "A=" + new string('x', EnvironmentReadLimits.MaxLogicalRecordBytes - 2);
        using (var result = Success(exactRecord, 1))
            Assert.Equal(EnvironmentReadLimits.MaxLogicalRecordBytes, result.SourceBytes);
        Limit(exactRecord + "x");

        var multibyte = "A=" + new string('é', (EnvironmentReadLimits.MaxLogicalRecordBytes - 2) / 2);
        using (var result = Success(multibyte, 1))
            Assert.Equal(EnvironmentReadLimits.MaxLogicalRecordBytes, result.SourceBytes);
        Limit(multibyte + "x");

        var continued = "A=" + new string('x', EnvironmentReadLimits.MaxLogicalRecordBytes - 4) + "\\\n";
        using (var result = Success(continued, 1))
            Assert.Equal(EnvironmentReadLimits.MaxLogicalRecordBytes, result.SourceBytes);
        Limit(continued.Insert(2, "x"));

        using (var result = Success(new string('x', EnvironmentReadLimits.MaxLogicalRecordBytes), 1))
            Assert.Equal(0, result.Assignments);
        Limit(new string('x', EnvironmentReadLimits.MaxLogicalRecordBytes + 1));

        var overriddenRecord = "A=" + new string('x', EnvironmentReadLimits.MaxLogicalRecordBytes - 3) + "\n";
        var overriddenSource = string.Concat(Enumerable.Repeat(overriddenRecord,
            EnvironmentReadLimits.MaxSourceBytes / EnvironmentReadLimits.MaxLogicalRecordBytes));
        using (var result = Success(overriddenSource, 1))
        {
            Assert.Equal(EnvironmentReadLimits.MaxSourceBytes, result.SourceBytes);
            Assert.Equal(EnvironmentReadLimits.MaxSourceBytes / EnvironmentReadLimits.MaxLogicalRecordBytes,
                result.Assignments);
            Assert.Single(result.Variables);
        }
        Limit(overriddenSource + "\n");
    }

    [Fact]
    public void CountsEveryAssignmentBeforeDuplicateReplacement()
    {
        var exact = string.Concat(Enumerable.Repeat("A=\n", EnvironmentReadLimits.MaxAssignments));
        using var result = Success(exact, 5);
        Assert.Equal(EnvironmentReadLimits.MaxAssignments, result.Assignments);
        Assert.Single(result.Variables);
        Limit(exact + "A=\n");

        using var remaining = Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
            Encoding.UTF8.GetBytes("A=1\nA=2"), 5, 2, TestContext.Current.CancellationToken));
        Assert.Equal(2, remaining.Assignments);
        Assert.Equal(EnvironmentReadFailureCode.LimitExceeded,
            Assert.IsType<EnvironmentFileParseResult.Failure>(EnvironmentFileParser.Parse(
                Encoding.UTF8.GetBytes("A=1\nA=2\nB=3"), 5, 2, TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public void CancellationWinsBeforeWorkAndDuringBothPasses()
    {
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            foreach (var bytes in new byte[][] { [], Encoding.UTF8.GetBytes("A=value"), [0xFF] })
            {
                var error = Assert.Throws<OperationCanceledException>(() =>
                    EnvironmentFileParser.Parse(bytes, 1, 1, canceled.Token));
                Assert.Equal(canceled.Token, error.CancellationToken);
            }
        }

        foreach (var phase in new[]
                 {
                     EnvironmentFileParser.Phase.TextValidation,
                     EnvironmentFileParser.Phase.StructureValidation,
                     EnvironmentFileParser.Phase.Decoding,
                     EnvironmentFileParser.Phase.BeforePublication,
                 })
        {
            using var cancellation = new CancellationTokenSource();
            var bytes = Encoding.UTF8.GetBytes("SECRET=value\nSECOND=value");
            var error = Assert.Throws<OperationCanceledException>(() => EnvironmentFileParser.Parse(
                bytes, 1, 2, (observed, offset) =>
                {
                    if (observed == phase && (phase == EnvironmentFileParser.Phase.BeforePublication || offset >= 13))
                        cancellation.Cancel();
                }, null, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
        }
    }

    [Fact]
    public void ContractsValidateArgumentsAndDisposeOwnedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentFileParser.Parse(
            [], 0, 0, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentFileParser.Parse(
            [], 1, -1, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentFileParser.Parse(
            [], 1, EnvironmentReadLimits.MaxAssignments + 1, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentFileParseResult.Failure(
            EnvironmentReadFailureCode.NotFound, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentFileParseResult.Failure(
            EnvironmentReadFailureCode.InvalidSource, 0));

        var marker = Guid.NewGuid().ToString("N");
        var result = Success("SECRET=" + marker, 1);
        var borrowed = result.Values.Reveal("SECRET");
        Assert.True(borrowed.SequenceEqual(marker));
        result.Dispose();
        Assert.True(IsCleared(borrowed));
        Assert.Throws<ObjectDisposedException>(() => { _ = result.Values.Reveal("SECRET"); });
    }

    [Fact]
    public void ClearsTemporaryLosingCanceledAndDisposedBuffers()
    {
        var observed = new List<Array>();
        using var cancellation = new CancellationTokenSource();
        var bytes = Encoding.UTF8.GetBytes("SECRET=first\nSECRET=second\nTHIRD=value");
        var error = Assert.Throws<OperationCanceledException>(() => EnvironmentFileParser.Parse(
            bytes, 1, 3, (phase, offset) =>
            {
                if (phase == EnvironmentFileParser.Phase.Decoding && offset >= 33)
                    cancellation.Cancel();
            }, observed.Add, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.NotEmpty(observed);
        Assert.All(observed, buffer => Assert.True(IsCleared(buffer), "cleared-buffer"));

        observed.Clear();
        var result = Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
            bytes, 1, 3, null, observed.Add, TestContext.Current.CancellationToken));
        result.Dispose();
        Assert.NotEmpty(observed);
        Assert.All(observed, buffer => Assert.True(IsCleared(buffer), "disposed-buffer"));
    }

    private static EnvironmentFileParseResult.Success Success(string source, int sourceId) =>
        Success(Encoding.UTF8.GetBytes(source), sourceId);

    private static EnvironmentFileParseResult.Success Success(byte[] source, int sourceId) =>
        Assert.IsType<EnvironmentFileParseResult.Success>(EnvironmentFileParser.Parse(
            source, sourceId, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken));

    private static EnvironmentFileParseResult.Failure Failure(string source, int sourceId) =>
        Assert.IsType<EnvironmentFileParseResult.Failure>(EnvironmentFileParser.Parse(
            Encoding.UTF8.GetBytes(source), sourceId, EnvironmentReadLimits.MaxAssignments,
            TestContext.Current.CancellationToken));

    private static void Limit(string source) => Limit(Encoding.UTF8.GetBytes(source));

    private static void Limit(byte[] source) => Assert.Equal(EnvironmentReadFailureCode.LimitExceeded,
        Assert.IsType<EnvironmentFileParseResult.Failure>(EnvironmentFileParser.Parse(
            source, 1, EnvironmentReadLimits.MaxAssignments, TestContext.Current.CancellationToken)).Code);

    private static void Equal(EnvironmentFileParseResult.Success result, string name, string expected) =>
        Assert.True(result.Values.Reveal(name).SequenceEqual(expected), name);

    private static bool IsCleared(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
            if (character != '\0')
                return false;
        return true;
    }

    private static bool IsCleared(Array buffer) => buffer switch
    {
        byte[] bytes => bytes.All(value => value == 0),
        char[] characters => characters.All(value => value == '\0'),
        _ => false,
    };
}
