using System.Reflection;
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

    [Theory]
    [InlineData("s")]
    [InlineData("as")]
    [InlineData("b")]
    [InlineData("a(sb)")]
    public void EveryFixedVariantPropertySignatureIsAccepted(string signature) =>
        MessageReader.EnsureVariantSignature(signature, signature);

    [Theory]
    [InlineData("b", "u")]
    [InlineData("as", "s")]
    [InlineData("a(sb)", "a(ss)")]
    public void NewFixedPropertyReadersRejectWrongVariantSignatures(
        string expected,
        string actual)
    {
        var exception = Assert.Throws<DBusUnexpectedValueException>(
            () => MessageReader.EnsureVariantSignature(actual, expected));
        Assert.Equal("The D-Bus reply signature is incompatible.", exception.Message);
    }

    [Fact]
    public void StringArrayReaderAcceptsTheFixedCountBoundary()
    {
        var message = VariantStringArrayMessage(["one", "two"]);

        var values = MessageReader.ReadVariantStringArray(
            message,
            maximumCount: 2,
            maximumItemBytes: 3,
            maximumTotalBytes: 6);

        Assert.Equal(["one", "two"], values);
    }

    [Fact]
    public void StringArrayReaderRejectsCountBoundaryPlusOneBeforeMaterializingIt()
    {
        var message = VariantStringArrayMessage(["one", "two", "x"]);

        var exception = Assert.Throws<DBusReplyLimitException>(() =>
            MessageReader.ReadVariantStringArray(
                message,
                maximumCount: 2,
                maximumItemBytes: 3,
                maximumTotalBytes: 7));

        Assert.Equal("The D-Bus reply exceeds its fixed bound.", exception.Message);
    }

    [Fact]
    public void StringArrayReaderRejectsEncodedSizeBoundaryPlusOne()
    {
        var message = VariantStringArrayMessage(["four"]);

        var exception = Assert.Throws<DBusReplyLimitException>(() =>
            MessageReader.ReadVariantStringArray(
                message,
                maximumCount: 1,
                maximumItemBytes: 3,
                maximumTotalBytes: 4));

        Assert.Equal("The D-Bus reply exceeds its fixed bound.", exception.Message);
    }

    [Fact]
    public void StringReaderAcceptsEncodedBoundaryAndRejectsBoundaryPlusOne()
    {
        Assert.Equal("four", MessageReader.ReadVariantString(VariantStringMessage("four"), 4));

        var exception = Assert.Throws<DBusReplyLimitException>(() =>
            MessageReader.ReadVariantString(VariantStringMessage("five!"), 4));

        Assert.Equal("The D-Bus reply exceeds its fixed bound.", exception.Message);
    }

    [Fact]
    public void EnvironmentFilesReaderAcceptsCountBoundaryAndRejectsBoundaryPlusOne()
    {
        var boundary = MessageReader.ReadVariantEnvironmentFiles(
            VariantEnvironmentFilesMessage([("/a", false), ("/b", true)]),
            maximumCount: 2,
            maximumPathBytes: 2,
            maximumTotalBytes: 4);
        Assert.Equal([("/a", false), ("/b", true)], boundary);

        var exception = Assert.Throws<DBusReplyLimitException>(() =>
            MessageReader.ReadVariantEnvironmentFiles(
                VariantEnvironmentFilesMessage([("/a", false), ("/b", true), ("/c", false)]),
                maximumCount: 2,
                maximumPathBytes: 2,
                maximumTotalBytes: 6));

        Assert.Equal("The D-Bus reply exceeds its fixed bound.", exception.Message);
    }

    private static Message VariantStringMessage(string value)
    {
        using var connection = new DBusConnection("unix:path=/unused");
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodReturnHeader(1, default, "v");
        writer.WriteVariantString(value);
        return Parse(writer.CreateMessage());
    }

    private static Message VariantEnvironmentFilesMessage(
        IReadOnlyList<(string Path, bool IgnoreErrors)> values)
    {
        using var connection = new DBusConnection("unix:path=/unused");
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodReturnHeader(1, default, "v");
        writer.WriteSignature("a(sb)");
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var value in values)
        {
            writer.WriteStructureStart();
            writer.WriteString(value.Path);
            writer.WriteBool(value.IgnoreErrors);
        }
        writer.WriteArrayEnd(array);
        return Parse(writer.CreateMessage());
    }

    private static Message VariantStringArrayMessage(string[] values)
    {
        using var connection = new DBusConnection("unix:path=/unused");
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodReturnHeader(1, default, "v");
        writer.WriteVariant(VariantValue.Array(values));
        return Parse(writer.CreateMessage());
    }

    private static Message Parse(MessageBuffer buffer)
    {
        var sequence = typeof(MessageBuffer)
            .GetProperty("Sequence", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(buffer)!;
        var readOnlySequence = sequence.GetType()
            .GetProperty("AsReadOnlySequence", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(sequence)!;
        var messagePoolType = typeof(Message).Assembly.GetType(
            "Tmds.DBus.Protocol.MessagePool",
            throwOnError: true)!;
        var messagePool = Activator.CreateInstance(messagePoolType)!;
        var parser = typeof(Message).GetMethod(
            "TryReadMessage",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Message)parser.Invoke(null, [messagePool, readOnlySequence, null, false])!;
    }
}
