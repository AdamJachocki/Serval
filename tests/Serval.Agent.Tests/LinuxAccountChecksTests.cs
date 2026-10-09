using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class LinuxAccountChecksTests
{
    [Fact]
    public async Task AuthenticationUsesNativeResultAndCurrentAccountCheck()
    {
        var account = new LinuxAccount("alice", 1001, 1001, [new LinuxGroup("users", 1001)]);
        var checks = new LinuxAccountChecks(
            (username, password) => username == "alice" && password == "correct" ? account : null,
            (_, uid) => uid == 1001,
            TimeSpan.FromSeconds(1));

        var accepted = await checks.AuthenticateAsync(
            "alice", "correct", TestContext.Current.CancellationToken);
        Assert.Equal(AgentResultStatus.Success, accepted.Status);
        Assert.Equal(account, accepted.Account);
        Assert.Equal(AgentResultStatus.Denied, (await checks.AuthenticateAsync(
            "alice", "wrong", TestContext.Current.CancellationToken)).Status);
        Assert.Equal(AgentResultStatus.Denied, (await checks.AuthenticateAsync(
            "missing", "correct", TestContext.Current.CancellationToken)).Status);
        Assert.True(await checks.IsCurrentAsync("alice", 1001, TestContext.Current.CancellationToken));
        Assert.False(await checks.IsCurrentAsync("alice", 1002, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LateNativeSuccessCannotProduceLoginResult()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var account = new LinuxAccount("alice", 1001, 1001, []);
        var checks = new LinuxAccountChecks(
            (_, _) =>
            {
                entered.Set();
                release.Wait();
                return account;
            },
            (_, _) => true,
            TimeSpan.FromMilliseconds(100));

        var login = checks.AuthenticateAsync("alice", "secret", TestContext.Current.CancellationToken);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var result = await login;
            Assert.Equal(AgentResultStatus.Unavailable, result.Status);
            Assert.Null(result.Account);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task NativeFailureAndWorkerExhaustionAreUnavailable()
    {
        var failing = new LinuxAccountChecks((_, _) => throw new InvalidOperationException(),
            (_, _) => false, TimeSpan.FromSeconds(1));
        Assert.Equal(AgentResultStatus.Unavailable, (await failing.AuthenticateAsync(
            "alice", "synthetic", TestContext.Current.CancellationToken)).Status);
        Assert.False(await failing.IsCurrentAsync("alice", 1001, TestContext.Current.CancellationToken));

        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        var blocked = new LinuxAccountChecks((_, _) =>
        {
            entered.Signal();
            release.Wait();
            return null;
        }, (_, _) => true, TimeSpan.FromMilliseconds(200));
        var calls = Enumerable.Range(0, 4).Select(_ => blocked.AuthenticateAsync(
            "alice", "synthetic", TestContext.Current.CancellationToken)).ToArray();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var exhausted = await blocked.AuthenticateAsync(
                "alice", "synthetic", TestContext.Current.CancellationToken);
            Assert.Equal(AgentResultStatus.Unavailable, exhausted.Status);
            Assert.All(await Task.WhenAll(calls), result =>
                Assert.Equal(AgentResultStatus.Unavailable, result.Status));
        }
        finally
        {
            release.Set();
        }
    }
}
