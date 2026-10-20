using System.Buffers.Binary;
using System.Text;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Application.Tests.Ipc;

public sealed class AgentProtocolTests
{
    [Theory]
    [InlineData("{\"version\":2,\"operation\":\"Login\",\"correlationId\":\"abc\",\"username\":\"alice\",\"password\":\"test\"}", typeof(AgentRequest.Login))]
    [InlineData("{\"version\":2,\"operation\":\"Logout\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.Logout))]
    [InlineData("{\"version\":2,\"operation\":\"ValidateSession\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.ValidateSession))]
    [InlineData("{\"version\":2,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.ListServices))]
    [InlineData("{\"version\":2,\"operation\":\"InspectService\",\"correlationId\":\"abc\",\"session\":\"token\",\"service\":\"worker.service\"}", typeof(AgentRequest.InspectService))]
    [InlineData("{\"version\":2,\"operation\":\"ListGrants\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.ListGrants))]
    [InlineData("{\"version\":2,\"operation\":\"AddGrant\",\"correlationId\":\"abc\",\"session\":\"token\",\"subjectKind\":\"User\",\"subject\":\"alice\",\"grantOperation\":\"Service.View\",\"service\":\"worker.service\"}", typeof(AgentRequest.AddGrant))]
    [InlineData("{\"version\":2,\"operation\":\"RemoveGrant\",\"correlationId\":\"abc\",\"session\":\"token\",\"grantId\":1}", typeof(AgentRequest.RemoveGrant))]
    public void AcceptsOnlyEightTypedOperations(string json, Type expected)
    {
        Assert.IsType(expected, AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\"}")]
    [InlineData("{\"version\":3,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\"}")]
    [InlineData("{\"version\":2,\"operation\":\"ExecuteCommand\",\"correlationId\":\"abc\",\"session\":\"token\"}")]
    [InlineData("{\"version\":2,\"operation\":\"ValidateSession\",\"correlationId\":\"abc\",\"session\":\"token\",\"uid\":0}")]
    [InlineData("{\"version\":2,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",\"session\":\"other\"}")]
    [InlineData("{\"version\":2,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",\"isAuthorized\":true}")]
    [InlineData("{\"version\":2,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",}")]
    public void RejectsUnexpectedOrMalformedFields(string json)
    {
        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void RejectsInvalidUtf8AndOversizedPayload()
    {
        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest([0xff, 0xfe]));
        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(new byte[AgentProtocol.MaximumFrameBytes + 1]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AgentProtocol.MaximumFrameBytes + 1)]
    public async Task RejectsInvalidFrameLengthBeforeReadingPayload(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        await using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<AgentProtocolException>(
            () => AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsTruncatedFrame()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 100);
        await using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<EndOfStreamException>(
            () => AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RoundTripsTypedRequest()
    {
        await using var stream = new MemoryStream();
        await AgentProtocol.WriteRequestAsync(stream,
            new AgentRequest.InspectService("c-1", "token", "worker.service"),
            TestContext.Current.CancellationToken);
        stream.Position = 0;
        var request = Assert.IsType<AgentRequest.InspectService>(
            await AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal("worker.service", request.Service);
    }

    [Fact]
    public void AcceptsControlCharactersInPasswordButRejectsNul()
    {
        var accepted = AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(
            """{"version":2,"operation":"Login","correlationId":"abc","username":"alice","password":"line\nnext"}"""));
        Assert.Equal("line\nnext", Assert.IsType<AgentRequest.Login>(accepted).Password);

        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(
            """{"version":2,"operation":"Login","correlationId":"abc","username":"alice","password":"a\u0000b"}""")));
    }

    [Fact]
    public async Task InvalidRequestFailureIsSmallAndContainsNoInput()
    {
        await using var stream = new MemoryStream();
        await AgentProtocol.WriteInvalidRequestAsync(stream, TestContext.Current.CancellationToken);
        Assert.InRange(stream.Length, 5, 128);
        Assert.Contains("InvalidRequest", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task ReadsTypedSuccessAndSanitizedFailure()
    {
        var request = new AgentRequest.ListServices("abc", "opaque");
        await using var success = new MemoryStream();
        await AgentProtocol.WriteResultAsync(success,
            new AgentResult.ListServices(AgentResultCode.Success,
                [new AgentService("worker.service", "Worker", "loaded", "active", "running")]),
            TestContext.Current.CancellationToken);
        success.Position = 0;
        var result = Assert.IsType<AgentResult.ListServices>(await AgentProtocol.ReadResultAsync(
            success, request, TestContext.Current.CancellationToken));
        Assert.Equal("worker.service", Assert.Single(result.Services!).Id);

        await using var failure = new MemoryStream();
        await AgentProtocol.WriteFailureAsync(failure, AgentResultCode.Unavailable,
            TestContext.Current.CancellationToken);
        failure.Position = 0;
        var unavailable = Assert.IsType<AgentResult.ListServices>(await AgentProtocol.ReadResultAsync(
            failure, request, TestContext.Current.CancellationToken));
        Assert.Equal(AgentResultCode.Unavailable, unavailable.Code);
        Assert.Null(unavailable.Services);
    }

    [Fact]
    public async Task RoundTripsCodeOnlyFailuresForEveryOperation()
    {
        (AgentRequest Request, AgentResult Result)[] cases =
        [
            (new AgentRequest.Login("login", "alice", "secret"),
                new AgentResult.Login(AgentResultCode.Denied, null, null)),
            (new AgentRequest.Logout("logout", "opaque"),
                new AgentResult.Logout(AgentResultCode.Unauthenticated)),
            (new AgentRequest.ValidateSession("validate", "opaque"),
                new AgentResult.ValidateSession(AgentResultCode.Unauthenticated, null)),
            (new AgentRequest.ListServices("services", "opaque"),
                new AgentResult.ListServices(AgentResultCode.Unauthenticated, null)),
            (new AgentRequest.InspectService("inspect", "opaque", "worker.service"),
                new AgentResult.InspectService(AgentResultCode.NotFound, null)),
            (new AgentRequest.ListGrants("grants", "opaque"),
                new AgentResult.ListGrants(AgentResultCode.Denied, null)),
            (new AgentRequest.AddGrant("add", "opaque", "User", "alice", "Service.View", "worker.service"),
                new AgentResult.AddGrant(AgentResultCode.InvalidRequest, null)),
            (new AgentRequest.RemoveGrant("remove", "opaque", 1),
                new AgentResult.RemoveGrant(AgentResultCode.Unavailable)),
        ];

        foreach (var (request, expected) in cases)
        {
            await using var stream = new MemoryStream();
            await AgentProtocol.WriteResultAsync(stream, expected, TestContext.Current.CancellationToken);

            var payload = stream.ToArray().AsSpan(4);
            Assert.Equal($"{{\"code\":\"{expected.Code}\"}}", Encoding.UTF8.GetString(payload));

            stream.Position = 0;
            var actual = await AgentProtocol.ReadResultAsync(
                stream, request, TestContext.Current.CancellationToken);
            Assert.IsType(expected.GetType(), actual);
            Assert.Equal(expected.Code, actual.Code);
        }
    }

    [Fact]
    public async Task RoundTripsConfirmedIdentityForLoginAndValidation()
    {
        var identity = new AgentIdentity("alice", 1001);
        await using var loginStream = new MemoryStream();
        await AgentProtocol.WriteResultAsync(loginStream,
            new AgentResult.Login(AgentResultCode.Success, "opaque-token", identity),
            TestContext.Current.CancellationToken);
        loginStream.Position = 0;
        var login = Assert.IsType<AgentResult.Login>(await AgentProtocol.ReadResultAsync(loginStream,
            new AgentRequest.Login("abc", "submitted", "secret"), TestContext.Current.CancellationToken));
        Assert.Equal(identity, login.Identity);

        await using var validationStream = new MemoryStream();
        await AgentProtocol.WriteResultAsync(validationStream,
            new AgentResult.ValidateSession(AgentResultCode.Success, identity),
            TestContext.Current.CancellationToken);
        validationStream.Position = 0;
        var validation = Assert.IsType<AgentResult.ValidateSession>(await AgentProtocol.ReadResultAsync(
            validationStream, new AgentRequest.ValidateSession("abc", "opaque-token"),
            TestContext.Current.CancellationToken));
        Assert.Equal(identity, validation.Identity);
    }

    [Theory]
    [InlineData("{\"code\":\"Success\",\"identity\":null}")]
    [InlineData("{\"code\":\"Success\",\"identity\":{\"name\":\"alice\",\"uid\":1001,\"role\":\"root\"}}")]
    [InlineData("{\"code\":\"Success\",\"identity\":{\"name\":\"alice\"}}")]
    [InlineData("{\"code\":\"Success\",\"identity\":{\"name\":\"alice\",\"uid\":-1}}")]
    public async Task RejectsMalformedValidationSuccess(string json)
    {
        await using var stream = Framed(json);
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.ValidateSession("abc", "opaque-token"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsOversizedConfirmedIdentity()
    {
        var json = $"{{\"code\":\"Success\",\"identity\":{{\"name\":\"{new string('a', 129)}\",\"uid\":1001}}}}";
        await using var stream = Framed(json);
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.ValidateSession("abc", "opaque-token"), TestContext.Current.CancellationToken));
    }

    private static MemoryStream Framed(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(4));
        return new MemoryStream(frame);
    }
}
