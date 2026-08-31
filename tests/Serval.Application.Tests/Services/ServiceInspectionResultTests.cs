using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Application.Tests.Services;

public sealed class ServiceInspectionResultTests
{
    [Fact]
    public void FoundStoresInspectedService()
    {
        var service = CreateService();

        var result = new ServiceInspectionResult.Found(service);

        Assert.Equal(service, result.Service);
    }

    [Fact]
    public void FoundRejectsNullService()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceInspectionResult.Found(null!));
    }

    [Fact]
    public void NotFoundStoresRequestedServiceId()
    {
        var serviceId = new SystemServiceId("missing.service");

        var result = new ServiceInspectionResult.NotFound(serviceId);

        Assert.Equal(serviceId, result.ServiceId);
    }

    [Fact]
    public void NotFoundRejectsNullServiceId()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceInspectionResult.NotFound(null!));
    }

    private static SystemService CreateService() =>
        new(
            new SystemServiceId("postgresql.service"),
            "PostgreSQL database server",
            new SystemdLoadState("loaded"),
            new SystemdActiveState("active"),
            new SystemdSubState("running"));
}
