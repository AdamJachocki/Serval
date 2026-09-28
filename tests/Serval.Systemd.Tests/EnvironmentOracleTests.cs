using System.Buffers.Binary;
using Serval.EnvironmentOracle;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class EnvironmentOracleTests
{
    [Fact]
    public void ManifestRoundTripsControlledValues()
    {
        var specifications = new[]
        {
            new ExpectationSpec("EMPTY", string.Empty),
            new ExpectationSpec("UNICODE", "zażółć"),
            new ExpectationSpec("QUOTED", "'alpha' \"beta\""),
            new ExpectationSpec("MULTILINE", "first\nsecond"),
            new ExpectationSpec("ABSENT", null),
        };

        using var stream = new MemoryStream();
        ExpectationManifest.Write(stream, specifications);
        stream.Position = 0;

        using var manifest = ExpectationManifest.Read(stream);

        Assert.True(specifications.Length == manifest.Entries.Count, "manifest-roundtrip");
        for (var index = 0; index < specifications.Length; index++)
        {
            Assert.True(
                string.Equals(
                    specifications[index].Name,
                    manifest.Entries[index].Name,
                    StringComparison.Ordinal),
                "manifest-roundtrip");
            Assert.True(
                (specifications[index].Value is not null) == manifest.Entries[index].IsPresent,
                "manifest-roundtrip");
            if (specifications[index].Value is not null)
            {
                Assert.True(
                    string.Equals(
                        specifications[index].Value,
                        System.Text.Encoding.UTF8.GetString(
                            manifest.GetValueBytes(manifest.Entries[index])),
                        StringComparison.Ordinal),
                    "manifest-roundtrip");
            }
        }
    }

    [Fact]
    public void ManifestAllowsExactLimitAndRejectsFirstExcessByte()
    {
        var exact = CreateExactLimitManifest();
        using (var stream = new MemoryStream(exact, writable: false))
        using (var manifest = ExpectationManifest.Read(stream))
        {
            Assert.Single(manifest.Entries);
        }

        Array.Resize(ref exact, exact.Length + 1);
        using var excessStream = new MemoryStream(exact, writable: false);
        var exception = Assert.Throws<ExpectationManifestException>(() => ExpectationManifest.Read(excessStream));
        Assert.Equal(ExpectationManifestError.OverLimit, exception.Error);
    }

    [Fact]
    public void ManifestDisposalClearsOwnedBuffer()
    {
        using var stream = new MemoryStream();
        ExpectationManifest.Write(stream, [new ExpectationSpec("CONTROLLED", "sensitive-synthetic-value")]);
        stream.Position = 0;
        var manifest = ExpectationManifest.Read(stream);

        manifest.Dispose();

        Assert.True(manifest.OwnedBufferIsClearedForTests());
        manifest.Dispose();
    }

    [Fact]
    public void OracleReturnsFixedComparisonStatuses()
    {
        using var fixture = OracleFixture.Create([new ExpectationSpec("CONTROLLED", "expected")]);

        var success = OracleApplication.Run(
            [], fixture.DirectoryPath, name => name == "CONTROLLED" ? "expected" : null);
        var mismatch = OracleApplication.Run(
            [], fixture.DirectoryPath, name => name == "CONTROLLED" ? "different" : null);

        Assert.True(success == OracleExitCode.Success, "oracle-status");
        Assert.True(mismatch == OracleExitCode.Mismatch, "oracle-status");
    }

    [Fact]
    public void OracleReturnsFixedMissingCredentialStatus()
    {
        var result = OracleApplication.Run([], null, _ => null);

        Assert.True(result == OracleExitCode.MissingCredential, "oracle-status");
    }

    [Fact]
    public void OracleReturnsFixedManifestFailureStatuses()
    {
        var duplicate = CreateManifest(
            ("DUPLICATE", "one"),
            ("DUPLICATE", "two"));
        var trailing = CreateManifest(("VALID", "value"));
        Array.Resize(ref trailing, trailing.Length + 1);
        var overLimit = new byte[ExpectationManifest.MaximumBytes + 1];

        var cases = new (byte[] Manifest, OracleExitCode Expected)[]
        {
            ([0x01, 0x02], OracleExitCode.MalformedManifest),
            (duplicate, OracleExitCode.DuplicateName),
            (trailing, OracleExitCode.TrailingData),
            (overLimit, OracleExitCode.OverLimit),
        };
        foreach (var item in cases)
        {
            using var fixture = OracleFixture.CreateRaw(item.Manifest);

            var result = OracleApplication.Run([], fixture.DirectoryPath, _ => null);

            Assert.True(result == item.Expected, "oracle-status");
        }
    }

    private static byte[] CreateExactLimitManifest()
    {
        const int fixedBytes = 8 + sizeof(uint) + 1 + sizeof(uint) + sizeof(uint) + 1;
        var result = new byte[ExpectationManifest.MaximumBytes];
        "SERVALM1"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 1);
        result[12] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(13), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(17), ExpectationManifest.MaximumBytes - fixedBytes);
        result[21] = (byte)'A';
        result.AsSpan(22).Fill((byte)'v');
        return result;
    }

    private static byte[] CreateManifest(params (string Name, string Value)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write("SERVALM1"u8);
        WriteUInt32(stream, entries.Length);
        foreach (var entry in entries)
        {
            var name = System.Text.Encoding.UTF8.GetBytes(entry.Name);
            var value = System.Text.Encoding.UTF8.GetBytes(entry.Value);
            stream.WriteByte(1);
            WriteUInt32(stream, name.Length);
            WriteUInt32(stream, value.Length);
            stream.Write(name);
            stream.Write(value);
        }

        return stream.ToArray();
    }

    private static void WriteUInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
        stream.Write(bytes);
    }

    private sealed class OracleFixture : IDisposable
    {
        private OracleFixture(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        internal string DirectoryPath { get; }

        internal static OracleFixture Create(IReadOnlyList<ExpectationSpec> expectations)
        {
            var fixture = CreateDirectory();
            using var destination = File.Create(Path.Combine(fixture.DirectoryPath, ExpectationManifest.CredentialName));
            ExpectationManifest.Write(destination, expectations);
            return fixture;
        }

        internal static OracleFixture CreateRaw(byte[] contents)
        {
            var fixture = CreateDirectory();
            File.WriteAllBytes(Path.Combine(fixture.DirectoryPath, ExpectationManifest.CredentialName), contents);
            return fixture;
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }

        private static OracleFixture CreateDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "serval-oracle-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new OracleFixture(directory);
        }
    }
}
