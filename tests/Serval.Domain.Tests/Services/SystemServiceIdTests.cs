using Serval.Domain.Services;
using Xunit;

namespace Serval.Domain.Tests.Services;

public sealed class SystemServiceIdTests
{
    [Theory]
    [InlineData("postgresql.service")]
    [InlineData("backup@.service")]
    [InlineData("postgresql@main.service")]
    [InlineData("dbus-org.freedesktop.resolve1.service")]
    [InlineData("serial-getty@ttyS0.service")]
    [InlineData(@"escaped\x2dname.service")]
    public void ConstructorAcceptsValidServiceUnitName(string value)
    {
        var id = new SystemServiceId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(id.Value, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".service")]
    [InlineData("@instance.service")]
    [InlineData("postgresql")]
    [InlineData("postgresql.socket")]
    [InlineData("postgresql.Service")]
    [InlineData("postgresql.service ")]
    [InlineData("postgres ql.service")]
    [InlineData("postgresql*.service")]
    [InlineData("żurnal.service")]
    [InlineData("../postgresql.service")]
    [InlineData("/etc/systemd/system/postgresql.service")]
    [InlineData("postgresql/../redis.service")]
    [InlineData("postgresql\0.service")]
    [InlineData("postgresql\n.service")]
    public void ConstructorRejectsInvalidOrMaliciousUnitName(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SystemServiceId(value!));
    }

    [Fact]
    public void ConstructorAcceptsMaximumLengthUnitName()
    {
        var value = $"{new string('a', 247)}.service";

        var id = new SystemServiceId(value);

        Assert.Equal(255, id.Value.Length);
    }

    [Fact]
    public void ConstructorRejectsUnitNameLongerThanMaximum()
    {
        var value = $"{new string('a', 248)}.service";

        Assert.Throws<ArgumentException>(() => new SystemServiceId(value));
    }

    [Fact]
    public void ConstructorDoesNotNormalizeInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new SystemServiceId(" postgresql.service "));
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
