using Serval.Domain.Services;
using Xunit;

namespace Serval.Domain.Tests.Services;

public sealed class SystemServiceIdTests
{
    [Fact]
    public void ConstructorPreservesCanonicalUnitName()
    {
        var id = new SystemServiceId("postgresql@main.service");

        Assert.Equal("postgresql@main.service", id.Value);
        Assert.Equal(id.Value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsMissingUnitName(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SystemServiceId(value!));
    }

    [Fact]
    public void EqualIdsHaveValueEquality()
    {
        var first = new SystemServiceId("postgresql.service");
        var second = new SystemServiceId("postgresql.service");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void IdEqualityIsCaseSensitive()
    {
        var first = new SystemServiceId("postgresql.service");
        var second = new SystemServiceId("PostgreSQL.service");

        Assert.NotEqual(first, second);
    }
}
