using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Serval.Application.Ipc;
using Serval.Application.Services;
using Xunit;

namespace Serval.Application.Tests.Ipc;

public sealed class EnvironmentProtocolTests
{
    [Fact]
    public async Task MetadataRoundTripsAndRequiresCompleteStrictFields()
    {
        var request = new AgentRequest.ListEnvironment("test", "session", "worker.service");
        var result = new AgentResult.ListEnvironment(AgentResultCode.Success,
            new AgentEnvironment("worker.service", EnvironmentModelScope.SupportedDeclarations,
                [new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment)],
                [new EnvironmentVariableMetadata("CONFIG", 0)], true));
        using var wire = new MemoryStream();
        await AgentProtocol.WriteResultAsync(wire, result, TestContext.Current.CancellationToken);
        wire.Position = 0;
        var copy = Assert.IsType<AgentResult.ListEnvironment>(await AgentProtocol.ReadResultAsync(wire, request, TestContext.Current.CancellationToken));
        Assert.Equal("CONFIG", Assert.Single(copy.Environment!.Variables).Name);
        Assert.True(copy.Environment.CanReveal);
        var validJson = Encoding.UTF8.GetString(wire.ToArray().AsSpan(4));
        foreach (var invalid in new[]
        {
            validJson.Replace("\"CanReveal\":true", "\"CanReveal\":true,\"extra\":false", StringComparison.Ordinal),
            validJson.Replace(",\"CanReveal\":true", "", StringComparison.Ordinal),
            validJson.Replace("\"WinningSourceId\":0", "\"WinningSourceId\":0,\"WinningSourceId\":0", StringComparison.Ordinal),
        })
        {
            using var malformed = new MemoryStream();
            var bytes = Encoding.UTF8.GetBytes(invalid);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
            malformed.Write(header);
            malformed.Write(bytes);
            malformed.Position = 0;
            await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(malformed, request, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulAndCanceledWritesClearBorrowedTransportStorage(bool cancel)
    {
        using var variable = new RevealedEnvironmentVariable("worker.service", "CONFIG",
            new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment), Guid.NewGuid().ToString("N").AsSpan());
        using var result = new AgentResult.RevealEnvironmentVariable(AgentResultCode.Success, variable);
        using var target = new CapturingStream(cancel);
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() => AgentProtocol.WriteResultAsync(target, result, TestContext.Current.CancellationToken));
        else
            await AgentProtocol.WriteResultAsync(target, result, TestContext.Current.CancellationToken);
        Assert.True(target.Payload.Length > 0);
        Assert.True(target.Payload.Span.IndexOfAnyExcept((byte)0) < 0);
        var field = typeof(RevealedEnvironmentVariable).GetField("value", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var owned = (char[])field.GetValue(variable)!;
        variable.Dispose();
        Assert.True(owned.AsSpan().IndexOfAnyExcept('\0') < 0);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => AgentProtocol.WriteResultAsync(output, result, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    private sealed class CapturingStream(bool cancel) : MemoryStream
    {
        public ReadOnlyMemory<byte> Payload { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 4)
            {
                Payload = buffer;
                if (cancel) throw new OperationCanceledException();
            }
            return ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public async Task BoundedNamesRoundTrip(int length)
    {
        var request = new AgentRequest.RevealEnvironmentVariable("test", "session", "worker.service", new string('A', length));
        using var stream = new MemoryStream();
        await AgentProtocol.WriteRequestAsync(stream, request, TestContext.Current.CancellationToken);
        stream.Position = 0;
        Assert.Equal(request, await AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../FILE")]
    [InlineData("1NAME")]
    [InlineData("A B")]
    [InlineData("é")]
    public void InvalidNamesFailBeforeDispatch(string name)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            operation = "RevealEnvironmentVariable",
            correlationId = "test",
            session = "session",
            service = "worker.service",
            name
        });
        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(bytes));
    }

    [Fact]
    public void ExcessiveNameAndDuplicateOrUnknownFieldsFail()
    {
        var json = "{\"version\":1,\"operation\":\"RevealEnvironmentVariable\",\"correlationId\":\"test\",\"session\":\"session\",\"service\":\"worker.service\",\"name\":\"CONFIG\"";
        foreach (var suffix in new[] { ",\"name\":\"OTHER\"}", ",\"path\":\"file\"}" })
            Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(json + suffix)));
        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(
            json.Replace("CONFIG", new string('A', 4097), StringComparison.Ordinal) + "}")));
    }

    [Fact]
    public async Task OneValueRoundTripsOnlyThroughExplicitCodecAndDisposalClearsAccess()
    {
        var secret = Guid.NewGuid().ToString("N");
        using var variable = new RevealedEnvironmentVariable("worker.service", "CONFIG",
            new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment), secret.AsSpan());
        using var result = new AgentResult.RevealEnvironmentVariable(AgentResultCode.Success, variable);
        Assert.False(JsonSerializer.Serialize(result).Contains(secret, StringComparison.Ordinal));
        Assert.False(result.ToString().Contains(secret, StringComparison.Ordinal));
        using var stream = new MemoryStream();
        await AgentProtocol.WriteResultAsync(stream, result, TestContext.Current.CancellationToken);
        stream.Position = 0;
        using var copy = Assert.IsType<AgentResult.RevealEnvironmentVariable>(await AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.RevealEnvironmentVariable("test", "session", "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
        using var http = new MemoryStream();
        await copy.Variable!.WriteHttpAsync(http, TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(http.ToArray());
        Assert.True(document.RootElement.GetProperty("value").GetString() == secret);
        variable.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => variable.WriteHttpAsync(http, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"code\":\"Denied\",\"variable\":{}}")]
    [InlineData("{\"code\":\"Denied\",\"code\":\"Denied\"}")]
    [InlineData("{\"code\":\"Unavailable\",\"failure\":\"arbitrary\"}")]
    public async Task FailureResponsesCannotCarryData(string json)
    {
        using var stream = new MemoryStream();
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        stream.Write(header);
        stream.Write(bytes);
        stream.Position = 0;
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.RevealEnvironmentVariable("test", "session", "worker.service", "CONFIG"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OversizeResultPublishesNoFrameAndWriteFailureClearsOwnedBuffer()
    {
        using var oversized = new AgentResult.RevealEnvironmentVariable(AgentResultCode.Success,
            new RevealedEnvironmentVariable("worker.service", "CONFIG",
                new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment), new string('x', AgentProtocol.MaximumResultBytes).AsSpan()));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.WriteResultAsync(output, oversized, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
        using var normal = new AgentResult.RevealEnvironmentVariable(AgentResultCode.Success,
            new RevealedEnvironmentVariable("worker.service", "CONFIG",
                new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment), Guid.NewGuid().ToString("N").AsSpan()));
        using var failing = new FailingStream();
        await Assert.ThrowsAsync<IOException>(() => AgentProtocol.WriteResultAsync(failing, normal, TestContext.Current.CancellationToken));
        Assert.True(failing.Payload.Span.IndexOfAnyExcept((byte)0) < 0);
    }

    private sealed class FailingStream : MemoryStream
    {
        public ReadOnlyMemory<byte> Payload { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 4)
            {
                Payload = buffer;
                throw new IOException("Synthetic write failure.");
            }
            return ValueTask.CompletedTask;
        }
    }
}
