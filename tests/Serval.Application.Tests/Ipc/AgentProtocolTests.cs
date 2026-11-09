using System.Buffers.Binary;
using System.Text;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Application.Tests.Ipc;

public sealed class AgentProtocolTests
{
    [Theory]
    [InlineData("{\"version\":1,\"operation\":\"Login\",\"correlationId\":\"abc\",\"username\":\"alice\",\"password\":\"test\"}", typeof(AgentRequest.Login))]
    [InlineData("{\"version\":1,\"operation\":\"Logout\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.Logout))]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.ListServices))]
    [InlineData("{\"version\":1,\"operation\":\"InspectService\",\"correlationId\":\"abc\",\"session\":\"token\",\"service\":\"worker.service\"}", typeof(AgentRequest.InspectService))]
    [InlineData("{\"version\":1,\"operation\":\"ListGrants\",\"correlationId\":\"abc\",\"session\":\"token\"}", typeof(AgentRequest.ListGrants))]
    [InlineData("{\"version\":1,\"operation\":\"AddGrant\",\"correlationId\":\"abc\",\"session\":\"token\",\"subjectKind\":\"User\",\"subject\":\"alice\",\"grantOperation\":\"Service.View\",\"service\":\"worker.service\"}", typeof(AgentRequest.AddGrant))]
    [InlineData("{\"version\":1,\"operation\":\"RemoveGrant\",\"correlationId\":\"abc\",\"session\":\"token\",\"grantId\":1}", typeof(AgentRequest.RemoveGrant))]
    [InlineData("{\"version\":1,\"operation\":\"AddFavorite\",\"correlationId\":\"abc\",\"session\":\"token\",\"service\":\"worker.service\"}", typeof(AgentRequest.AddFavorite))]
    [InlineData("{\"version\":1,\"operation\":\"RemoveFavorite\",\"correlationId\":\"abc\",\"session\":\"token\",\"service\":\"worker.service\"}", typeof(AgentRequest.RemoveFavorite))]
    public void AcceptsOnlyTypedOperations(string json, Type expected)
    {
        Assert.IsType(expected, AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("{\"version\":2,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\"}")]
    [InlineData("{\"version\":1,\"operation\":\"ExecuteCommand\",\"correlationId\":\"abc\",\"session\":\"token\"}")]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",\"uid\":0}")]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",\"session\":\"other\"}")]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",\"isAuthorized\":true}")]
    [InlineData("{\"version\":1,\"operation\":\"ListServices\",\"correlationId\":\"abc\",\"session\":\"token\",}")]
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
            """{"version":1,"operation":"Login","correlationId":"abc","username":"alice","password":"line\nnext"}"""));
        Assert.Equal("line\nnext", Assert.IsType<AgentRequest.Login>(accepted).Password);

        Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(
            """{"version":1,"operation":"Login","correlationId":"abc","username":"alice","password":"a\u0000b"}""")));
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

    [Theory]
    [InlineData("AddFavorite")]
    [InlineData("RemoveFavorite")]
    public void RejectsMalformedFavoriteRequests(string operation)
    {
        var overLength = new string('a', 256);
        string[] bodies =
        [
            ",\"session\":\"token\",\"service\":\"a.service\",\"uid\":0",
            ",\"session\":\"token\",\"service\":\"a.service\",\"user\":\"root\"",
            ",\"session\":\"token\",\"service\":\"a.service\",\"service\":\"b.service\"",
            ",\"session\":\"token\"",
            ",\"service\":\"a.service\"",
            ",\"session\":\"token\",\"service\":\"\"",
            ",\"session\":\"token\",\"service\":7",
            ",\"session\":\"token\",\"service\":\"" + overLength + "\"",
            ",\"session\":\"token\",\"service\":\"a\nb.service\"",
        ];
        foreach (var body in bodies)
        {
            var json = "{\"version\":1,\"operation\":\"" + operation + "\",\"correlationId\":\"abc\"" + body + "}";
            Assert.Throws<AgentProtocolException>(() => AgentProtocol.ParseRequest(Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public async Task RoundTripsFavoriteRequests()
    {
        await using var stream = new MemoryStream();
        await AgentProtocol.WriteRequestAsync(stream,
            new AgentRequest.AddFavorite("c-1", "token", "worker.service"), TestContext.Current.CancellationToken);
        await AgentProtocol.WriteRequestAsync(stream,
            new AgentRequest.RemoveFavorite("c-2", "token", "alias.service"), TestContext.Current.CancellationToken);
        stream.Position = 0;
        var add = Assert.IsType<AgentRequest.AddFavorite>(
            await AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
        var remove = Assert.IsType<AgentRequest.RemoveFavorite>(
            await AgentProtocol.ReadRequestAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal("worker.service", add.Service);
        Assert.Equal("alias.service", remove.Service);
    }

    [Fact]
    public async Task RoundTripsFavoritesOnListServicesSuccessOnly()
    {
        var request = new AgentRequest.ListServices("abc", "opaque");
        AgentService[] services =
        [
            new("a.service", "A", "loaded", "active", "running"),
            new("b.service", "B", "loaded", "inactive", "dead"),
        ];
        await using var success = new MemoryStream();
        await AgentProtocol.WriteResultAsync(success,
            new AgentResult.ListServices(AgentResultCode.Success, services, ["b.service"]),
            TestContext.Current.CancellationToken);
        success.Position = 0;
        var result = Assert.IsType<AgentResult.ListServices>(await AgentProtocol.ReadResultAsync(
            success, request, TestContext.Current.CancellationToken));
        Assert.Equal(["b.service"], result.Favorites);

        await using var failure = new MemoryStream();
        await AgentProtocol.WriteResultAsync(failure,
            new AgentResult.ListServices(AgentResultCode.Unavailable, null),
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("favorites", Encoding.UTF8.GetString(failure.ToArray()));
    }

    [Theory]
    [InlineData("""["c.service"]""")]
    [InlineData("""["a.service","a.service"]""")]
    [InlineData("""[7]""")]
    [InlineData("""[null]""")]
    [InlineData("\"a.service\"")]
    public async Task RejectsInvalidFavoritesInListServicesResult(string favorites)
    {
        var json = $$"""{"code":"Success","services":[{"Id":"a.service","Description":"A","LoadState":"loaded","ActiveState":"active","SubState":"running"}],"favorites":{{favorites}}}""";
        await using var stream = Framed(json);
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.ListServices("abc", "opaque"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(AgentResultCode.Success)]
    [InlineData(AgentResultCode.NotFound)]
    [InlineData(AgentResultCode.LimitReached)]
    [InlineData(AgentResultCode.Unavailable)]
    public async Task ReadsFavoriteResults(AgentResultCode code)
    {
        await using var stream = new MemoryStream();
        await AgentProtocol.WriteResultAsync(stream, new AgentResult.AddFavorite(code),
            TestContext.Current.CancellationToken);
        stream.Position = 0;
        var add = Assert.IsType<AgentResult.AddFavorite>(await AgentProtocol.ReadResultAsync(
            stream, new AgentRequest.AddFavorite("abc", "opaque", "a.service"),
            TestContext.Current.CancellationToken));
        Assert.Equal(code, add.Code);

        if (code == AgentResultCode.LimitReached)
        {
            return;
        }

        await using var removal = new MemoryStream();
        await AgentProtocol.WriteResultAsync(removal, new AgentResult.RemoveFavorite(code),
            TestContext.Current.CancellationToken);
        removal.Position = 0;
        Assert.Equal(code, Assert.IsType<AgentResult.RemoveFavorite>(await AgentProtocol.ReadResultAsync(
            removal, new AgentRequest.RemoveFavorite("abc", "opaque", "a.service"),
            TestContext.Current.CancellationToken)).Code);
    }

    [Fact]
    public async Task LimitReachedIsOnlyValidForAddFavorite()
    {
        await using var stream = Framed("""{"code":"LimitReached"}""");
        await Assert.ThrowsAsync<AgentProtocolException>(() => AgentProtocol.ReadResultAsync(stream,
            new AgentRequest.RemoveFavorite("abc", "opaque", "a.service"), TestContext.Current.CancellationToken));
    }

    private static MemoryStream Framed(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        return new MemoryStream([.. header, .. payload]);
    }
}
