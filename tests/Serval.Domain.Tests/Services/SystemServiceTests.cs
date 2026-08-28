using Serval.Domain.Services;
using Xunit;

namespace Serval.Domain.Tests.Services;

public sealed class SystemServiceTests
{
    [Fact]
    public void ConstructorStoresReadModelProperties()
    {
        var id = new SystemServiceId("postgresql.service");
        var loadState = new SystemdLoadState("loaded");
        var activeState = new SystemdActiveState("active");
        var subState = new SystemdSubState("running");

        var service = new SystemService(
            id,
            "PostgreSQL database server",
            loadState,
            activeState,
            subState);

        Assert.Equal(id, service.Id);
        Assert.Equal("PostgreSQL database server", service.Description);
        Assert.Equal(loadState, service.LoadState);
        Assert.Equal(activeState, service.ActiveState);
        Assert.Equal(subState, service.SubState);
    }

    [Fact]
    public void EqualServicesHaveValueEquality()
    {
        var first = CreateService();
        var second = CreateService();

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ServicesWithDifferentCanonicalNamesAreNotEqual()
    {
        var first = CreateService();
        var second = CreateService(new SystemServiceId("redis.service"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ConstructorRejectsNullComponents()
    {
        var service = CreateService();

        Assert.Throws<ArgumentNullException>(() =>
            new SystemService(null!, service.Description, service.LoadState, service.ActiveState, service.SubState));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemService(service.Id, null!, service.LoadState, service.ActiveState, service.SubState));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemService(service.Id, service.Description, null!, service.ActiveState, service.SubState));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemService(service.Id, service.Description, service.LoadState, null!, service.SubState));
        Assert.Throws<ArgumentNullException>(() =>
            new SystemService(service.Id, service.Description, service.LoadState, service.ActiveState, null!));
    }

    private static SystemService CreateService(SystemServiceId? id = null) =>
        new(
            id ?? new SystemServiceId("postgresql.service"),
            "PostgreSQL database server",
            new SystemdLoadState("loaded"),
            new SystemdActiveState("active"),
            new SystemdSubState("running"));
}
