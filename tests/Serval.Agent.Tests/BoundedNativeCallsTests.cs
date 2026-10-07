using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class BoundedNativeCallsTests
{
    [Fact]
    public async Task TimedOutNativeCallKeepsSlotUntilItActuallyReturns()
    {
        var calls = new BoundedNativeCalls(1, TimeSpan.FromMilliseconds(100));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = calls.RunAsync(() =>
        {
            entered.Set();
            release.Wait();
            return "late-success";
        }, TestContext.Current.CancellationToken);

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var timedOut = await first;
            Assert.False(timedOut.Succeeded);
            Assert.Null(timedOut.Value);

            var whileOccupied = await calls.RunAsync(() => "must-not-run", TestContext.Current.CancellationToken);
            Assert.False(whileOccupied.Succeeded);
        }
        finally
        {
            release.Set();
        }

        await AssertEventuallyAvailableAsync(calls);
    }

    [Fact]
    public async Task CompletedNativeCallReturnsResultAndReleasesSlot()
    {
        var calls = new BoundedNativeCalls(1, TimeSpan.FromSeconds(1));
        var first = await calls.RunAsync(() => 42, TestContext.Current.CancellationToken);
        var second = await calls.RunAsync(() => 43, TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(42, first.Value);
        Assert.True(second.Succeeded);
        Assert.Equal(43, second.Value);
    }

    private static async Task AssertEventuallyAvailableAsync(BoundedNativeCalls calls)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await calls.RunAsync(() => "available", deadline.Token);
            if (result.Succeeded)
            {
                Assert.Equal("available", result.Value);
                return;
            }

            await Task.Delay(10, deadline.Token);
        }
    }
}
