using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class SystemdServiceInventoryTests
{
    [Fact]
    public async Task ListReturnsCurrentMetadataInCanonicalOrdinalOrderWithClassification()
    {
        var ordinary = Service("zeta.service", "inactive", "dead");
        var protectedService = Service("ssh.service", "active", "running");
        var inventory = Create(
            new ServiceEnumerationSnapshot(
                [
                    new EnumeratedSystemService(ordinary, [ordinary.Id]),
                    new EnumeratedSystemService(protectedService, [protectedService.Id]),
                ],
                []));

        var services = await inventory.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["ssh.service", "zeta.service"], services.Select(service => service.Id.Value));
        Assert.Equal("active", services[0].ActiveState.Value);
        Assert.Equal("running", services[0].SubState.Value);
        Assert.True(services[0].IsProtected);
        Assert.False(services[1].IsProtected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectMapsExplicitResults(bool found)
    {
        var requested = new SystemServiceId("alias.service");
        var canonical = Service("systemd-journald.service", "active", "running");
        SystemServiceId? observed = null;
        var inventory = new SystemdServiceInventory(
            _ => throw new InvalidOperationException(),
            (serviceId, _) =>
            {
                observed = serviceId;
                return Task.FromResult<SystemdServiceInspectionResult>(found
                    ? new SystemdServiceInspectionResult.Found(canonical, [canonical.Id, requested])
                    : new SystemdServiceInspectionResult.NotFound(requested));
            });

        var result = await inventory.InspectAsync(requested, TestContext.Current.CancellationToken);

        Assert.Equal(requested, observed);
        if (found)
        {
            var mapped = Assert.IsType<ServiceInspectionResult.Found>(result).Service;
            Assert.Equal(canonical.Id, mapped.Id);
            Assert.True(mapped.IsProtected);
        }
        else
        {
            Assert.Equal(requested, Assert.IsType<ServiceInspectionResult.NotFound>(result).ServiceId);
        }
    }

    [Fact]
    public async Task ListingFailureIsNotPublishedAsPartialSuccess()
    {
        var failure = new SystemdDbusException(SystemdDbusFailureKind.Unavailable);
        var inventory = new SystemdServiceInventory(
            _ => Task.FromException<ServiceEnumerationSnapshot>(failure),
            (_, _) => throw new InvalidOperationException());

        var thrown = await Assert.ThrowsAsync<SystemdDbusException>(
            () => inventory.ListAsync(TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellationPropagatesThroughCompleteOperation(bool inspect)
    {
        using var cancellation = new CancellationTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Wait(CancellationToken token)
        {
            reached.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }

        var inventory = new SystemdServiceInventory(
            async token =>
            {
                await Wait(token);
                return new ServiceEnumerationSnapshot([], []);
            },
            async (serviceId, token) =>
            {
                await Wait(token);
                return new SystemdServiceInspectionResult.NotFound(serviceId);
            });
        var operation = inspect
            ? (Task)inventory.InspectAsync(new SystemServiceId("a.service"), cancellation.Token)
            : inventory.ListAsync(cancellation.Token);

        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task PrecancelledOperationsDoNotInvokeAdapterPrimitives()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;
        var inventory = new SystemdServiceInventory(
            _ =>
            {
                invoked = true;
                throw new InvalidOperationException();
            },
            (_, _) =>
            {
                invoked = true;
                throw new InvalidOperationException();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inventory.ListAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inventory.InspectAsync(new SystemServiceId("a.service"), cancellation.Token));
        Assert.False(invoked);
    }

    [Fact]
    public async Task InspectRejectsNullBeforeInvokingAdapterPrimitive()
    {
        var invoked = false;
        var inventory = new SystemdServiceInventory(
            _ => throw new InvalidOperationException(),
            (_, _) =>
            {
                invoked = true;
                throw new InvalidOperationException();
            });

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => inventory.InspectAsync(null!, TestContext.Current.CancellationToken));
        Assert.False(invoked);
    }

    private static SystemdServiceInventory Create(ServiceEnumerationSnapshot snapshot) =>
        new(
            _ => Task.FromResult(snapshot),
            (_, _) => throw new InvalidOperationException());

    private static SystemService Service(string id, string activeState, string subState) =>
        new(
            new SystemServiceId(id),
            $"Description for {id}",
            new SystemdLoadState("loaded"),
            new SystemdActiveState(activeState),
            new SystemdSubState(subState));
}
