using Tmds.DBus.Protocol;

namespace Serval.Systemd.DBus.Generated;

internal sealed class Manager : DBusObject
{
    internal const string Interface = "org.freedesktop.systemd1.Manager";

    internal Manager(DBusConnection connection, string destination, ObjectPath path)
        : base(connection, destination, path)
    {
    }

    internal Task<string> GetVersionAsync() =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage("Version"),
            static (Message message, object? _) => MessageReader.ReadVariantString(message),
            this);

    internal Task<(string, string)[]> ListUnitFilesAsync() =>
        Connection.CallMethodAsync(
            CreateMethodMessage("ListUnitFiles"),
            static (Message message, object? _) => MessageReader.ReadUnitFiles(message),
            this);

    internal Task<(string, string, string, string, string, string, ObjectPath, uint, string, ObjectPath)[]>
        ListUnitsByPatternsAsync(string[] states, string[] patterns) =>
        Connection.CallMethodAsync(
            CreateListUnitsByPatternsMessage(states, patterns),
            static (Message message, object? _) => MessageReader.ReadListedUnits(message),
            this);

    internal Task<(string, string, string, string, string, string, ObjectPath, uint, string, ObjectPath)[]>
        ListUnitsByNamesAsync(string[] names) =>
        Connection.CallMethodAsync(
            CreateListUnitsByNamesMessage(names),
            static (Message message, object? _) => MessageReader.ReadListedUnits(message),
            this);

    private MessageBuffer CreateMethodMessage(string member)
    {
        var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, Path, Interface, member);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateListUnitsByPatternsMessage(string[] states, string[] patterns)
    {
        var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Destination,
            path: Path,
            @interface: Interface,
            signature: "asas",
            member: "ListUnitsByPatterns");
        writer.WriteArray(states);
        writer.WriteArray(patterns);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateListUnitsByNamesMessage(string[] names)
    {
        var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Destination,
            path: Path,
            @interface: Interface,
            signature: "as",
            member: "ListUnitsByNames");
        writer.WriteArray(names);
        return writer.CreateMessage();
    }

    private MessageBuffer CreateGetPropertyMessage(string property)
    {
        var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Destination,
            path: Path,
            @interface: "org.freedesktop.DBus.Properties",
            signature: "ss",
            member: "Get");
        writer.WriteString(Interface);
        writer.WriteString(property);
        return writer.CreateMessage();
    }
}

internal sealed class Unit : DBusObject
{
    internal const string Interface = "org.freedesktop.systemd1.Unit";

    internal Unit(DBusConnection connection, string destination, ObjectPath path)
        : base(connection, destination, path)
    {
    }

    internal Task<string> GetIdAsync() => GetStringPropertyAsync("Id");

    internal Task<string[]> GetNamesAsync() =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage("Names"),
            static (Message message, object? _) => MessageReader.ReadVariantStringArray(message),
            this);

    internal Task<string> GetDescriptionAsync() => GetStringPropertyAsync("Description");

    internal Task<string> GetLoadStateAsync() => GetStringPropertyAsync("LoadState");

    internal Task<string> GetActiveStateAsync() => GetStringPropertyAsync("ActiveState");

    internal Task<string> GetSubStateAsync() => GetStringPropertyAsync("SubState");

    private Task<string> GetStringPropertyAsync(string property) =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage(property),
            static (Message message, object? _) => MessageReader.ReadVariantString(message),
            this);

    private MessageBuffer CreateGetPropertyMessage(string property)
    {
        var writer = Connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Destination,
            path: Path,
            @interface: "org.freedesktop.DBus.Properties",
            signature: "ss",
            member: "Get");
        writer.WriteString(Interface);
        writer.WriteString(property);
        return writer.CreateMessage();
    }
}

internal static class MessageReader
{
    internal static void EnsureSignature(string? actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new DBusUnexpectedValueException("The D-Bus reply signature is incompatible.");
        }
    }

    internal static string ReadVariantString(Message message)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        reader.ReadSignature("s"u8);
        return reader.ReadString();
    }

    internal static string[] ReadVariantStringArray(Message message)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        reader.ReadSignature("as"u8);
        return reader.ReadArrayOfString();
    }

    internal static (string, string)[] ReadUnitFiles(Message message)
    {
        EnsureSignature(message.SignatureAsString, "a(ss)");
        var reader = message.GetBodyReader();
        var entries = new List<(string, string)>();
        var arrayEnd = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(arrayEnd))
        {
            reader.AlignStruct();
            entries.Add((reader.ReadString(), reader.ReadString()));
        }

        return entries.ToArray();
    }

    internal static (string, string, string, string, string, string, ObjectPath, uint, string, ObjectPath)[]
        ReadListedUnits(Message message)
    {
        EnsureSignature(message.SignatureAsString, "a(ssssssouso)");
        var reader = message.GetBodyReader();
        var entries = new List<(
            string,
            string,
            string,
            string,
            string,
            string,
            ObjectPath,
            uint,
            string,
            ObjectPath)>();
        var arrayEnd = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(arrayEnd))
        {
            reader.AlignStruct();
            entries.Add((
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadObjectPath(),
                reader.ReadUInt32(),
                reader.ReadString(),
                reader.ReadObjectPath()));
        }

        return entries.ToArray();
    }
}
