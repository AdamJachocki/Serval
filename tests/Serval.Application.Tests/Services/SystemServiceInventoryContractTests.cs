using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Application.Tests.Services;

public sealed class SystemServiceInventoryContractTests
{
    [Fact]
    public void ListingContractUsesApplicationReadModelAndSupportsCancellation()
    {
        var method = typeof(ISystemServiceInventory).GetMethod(nameof(ISystemServiceInventory.ListAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<IReadOnlyList<SystemService>>), method.ReturnType);
        Assert.Equal(
            [typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void InspectionContractUsesExplicitResultAndSupportsCancellation()
    {
        var method = typeof(ISystemServiceInventory).GetMethod(nameof(ISystemServiceInventory.InspectAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<ServiceInspectionResult>), method.ReturnType);
        Assert.Equal(
            [typeof(SystemServiceId), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task ApplicationConsumerCanUseFakeInventoryWithoutSystemdDetails()
    {
        var service = new SystemService(
            new SystemServiceId("ssh.service"),
            "OpenSSH server",
            new SystemdLoadState("loaded"),
            new SystemdActiveState("active"),
            new SystemdSubState("running"),
            isProtected: true);
        ISystemServiceInventory inventory = new FakeSystemServiceInventory(service);

        var listed = await inventory.ListAsync(TestContext.Current.CancellationToken);
        var inspected = Assert.IsType<ServiceInspectionResult.Found>(
            await inventory.InspectAsync(service.Id, TestContext.Current.CancellationToken));

        Assert.Same(service, Assert.Single(listed));
        Assert.Same(service, inspected.Service);
        Assert.True(inspected.Service.IsProtected);
    }

    private sealed class FakeSystemServiceInventory(SystemService service) : ISystemServiceInventory
    {
        public Task<IReadOnlyList<SystemService>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SystemService>>([service]);
        }

        public Task<ServiceInspectionResult> InspectAsync(
            SystemServiceId serviceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ServiceInspectionResult>(serviceId == service.Id
                ? new ServiceInspectionResult.Found(service)
                : new ServiceInspectionResult.NotFound(serviceId));
        }
    }
}
