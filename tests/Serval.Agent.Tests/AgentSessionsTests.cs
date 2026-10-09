using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class AgentSessionsTests
{
    [Fact]
    public async Task ValidSessionUsesAgentIdentityAndForgedTokenIsRejected()
    {
        var validity = new FakeValidity();
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), validity, TimeProvider.System);
        var token = sessions.Create("alice", 1001);

        var principal = await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken);
        var forged = await sessions.AuthenticateAsync("fake", TestContext.Current.CancellationToken);

        Assert.Equal(new AgentPrincipal("alice", 1001), principal);
        Assert.Null(forged);
        Assert.Equal(("alice", (uint)1001), validity.LastChecked);
    }

    [Fact]
    public async Task LogoutRevokesTokenAndNewAgentCannotAcceptIt()
    {
        var validity = new FakeValidity();
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), validity, TimeProvider.System);
        var token = sessions.Create("alice", 1001);

        Assert.True(sessions.Revoke(token));
        Assert.False(sessions.Revoke(token));
        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));

        var otherAgent = new AgentSessions(TimeSpan.FromMinutes(15), validity, TimeProvider.System);
        Assert.Null(await otherAgent.AuthenticateAsync(token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdleExpiryUsesConfiguredIntervalAndAcceptedActivityResetsClock()
    {
        var time = new ManualTimeProvider();
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), new FakeValidity(), time);
        var token = sessions.Create("alice", 1001);

        time.Advance(TimeSpan.FromMinutes(14));
        Assert.NotNull(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
        sessions.RecordAcceptedActivity(token);
        time.Advance(TimeSpan.FromMinutes(14));
        Assert.NotNull(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
        sessions.RecordAcceptedActivity(token);
        time.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));

        var configured = new AgentSessions(TimeSpan.FromMinutes(1), new FakeValidity(), time);
        var configuredToken = configured.Create("bob", 1002);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await configured.AuthenticateAsync(configuredToken, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisabledOrReplacedAccountRevokesSession()
    {
        var validity = new FakeValidity { Current = false };
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), validity, TimeProvider.System);
        var token = sessions.Create("alice", 1001);

        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
        validity.Current = true;
        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NameToUidReplacementRevokesOriginalSession()
    {
        var validity = new FakeValidity { CurrentUid = 1001 };
        var sessions = new AgentSessions(TimeSpan.FromMinutes(15), validity, TimeProvider.System);
        var token = sessions.Create("alice", 1001);
        validity.CurrentUid = 2002;

        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
        validity.CurrentUid = 1001;
        Assert.Null(await sessions.AuthenticateAsync(token, TestContext.Current.CancellationToken));
    }

    private sealed class FakeValidity : IAgentAccountValidity
    {
        public bool Current { get; set; } = true;

        public uint? CurrentUid { get; set; }

        public (string Name, uint Uid)? LastChecked { get; private set; }

        public Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastChecked = (name, uid);
            return Task.FromResult(Current && (CurrentUid is null || CurrentUid == uid));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan interval) => ticks += interval.Ticks;
    }
}
