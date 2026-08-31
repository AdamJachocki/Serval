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
}
