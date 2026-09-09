using Serval.Systemd.DBus.Generated;
using Tmds.DBus.Protocol;
using Xunit;

namespace Serval.Systemd.Tests.DBus;

public sealed class SystemdDbusProxyTests
{
    [Fact]
    public void ExpectedReplySignatureIsAccepted()
    {
        MessageReader.EnsureSignature("a(ss)", "a(ss)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("s")]
    [InlineData("a(sss)")]
    public void IncompatibleReplySignatureIsRejectedWithoutEchoingIt(string? actual)
    {
        var exception = Assert.Throws<DBusUnexpectedValueException>(
            () => MessageReader.EnsureSignature(actual, "a(ss)"));

        Assert.Equal("The D-Bus reply signature is incompatible.", exception.Message);
    }
}
