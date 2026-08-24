using Serval.Domain.Services;
using Xunit;

namespace Serval.Domain.Tests.Services;

public sealed class SystemdStateTests
{
    [Fact]
    public void KnownLoadStateIsClassifiedAndPreserved()
    {
        var state = new SystemdLoadState("not-found");

        Assert.Equal(SystemdLoadStateKind.NotFound, state.Kind);
        Assert.Equal("not-found", state.Value);
    }

    [Fact]
    public void UnknownLoadStateIsPreserved()
    {
        var state = new SystemdLoadState("future-load-state");

        Assert.Equal(SystemdLoadStateKind.Unknown, state.Kind);
        Assert.Equal("future-load-state", state.Value);
        Assert.Equal(state.Value, state.ToString());
    }

    [Fact]
    public void KnownActiveStateIsClassifiedAndPreserved()
    {
        var state = new SystemdActiveState("maintenance");

        Assert.Equal(SystemdActiveStateKind.Maintenance, state.Kind);
        Assert.Equal("maintenance", state.Value);
    }

    [Fact]
    public void UnknownActiveStateIsPreserved()
    {
        var state = new SystemdActiveState("future-active-state");

        Assert.Equal(SystemdActiveStateKind.Unknown, state.Kind);
        Assert.Equal("future-active-state", state.Value);
        Assert.Equal(state.Value, state.ToString());
    }

    [Fact]
    public void ArbitrarySubStateIsPreserved()
    {
        var state = new SystemdSubState("vendor-specific-running");

        Assert.Equal("vendor-specific-running", state.Value);
        Assert.Equal(state.Value, state.ToString());
    }

    [Fact]
    public void StatesHaveValueEquality()
    {
        Assert.Equal(new SystemdLoadState("loaded"), new SystemdLoadState("loaded"));
        Assert.Equal(new SystemdActiveState("active"), new SystemdActiveState("active"));
        Assert.Equal(new SystemdSubState("running"), new SystemdSubState("running"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StateConstructorsRejectMissingValues(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SystemdLoadState(value!));
        Assert.ThrowsAny<ArgumentException>(() => new SystemdActiveState(value!));
        Assert.ThrowsAny<ArgumentException>(() => new SystemdSubState(value!));
    }
}
