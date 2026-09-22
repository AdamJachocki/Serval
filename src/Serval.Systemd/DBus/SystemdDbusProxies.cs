using System.Text;
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
            static (Message message, object? _) => MessageReader.ReadVariantString(message, 256),
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

    internal Task<string> GetIdAsync() => GetStringPropertyAsync("Id", 255);

    internal Task<string[]> GetNamesAsync() =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage("Names"),
            static (Message message, object? _) => MessageReader.ReadVariantStringArray(
                message,
                maximumCount: 65_536,
                maximumItemBytes: 255,
                maximumTotalBytes: 16_711_680),
            this);

    internal Task<string> GetDescriptionAsync() => GetStringPropertyAsync("Description", 65_536);

    internal Task<string> GetLoadStateAsync() => GetStringPropertyAsync("LoadState", 128);

    internal Task<string> GetActiveStateAsync() => GetStringPropertyAsync("ActiveState", 128);

    internal Task<string> GetSubStateAsync() => GetStringPropertyAsync("SubState", 128);

    internal Task<string> GetFragmentPathAsync() =>
        GetStringPropertyAsync("FragmentPath", EnvironmentReadLimits.MaxPathBytes + 1);

    internal Task<string[]> GetDropInPathsAsync() => GetStringArrayPropertyAsync(
        "DropInPaths",
        EnvironmentReadLimits.MaxConfigurationPaths,
        EnvironmentReadLimits.MaxPathBytes + 1,
        EnvironmentReadLimits.MaxConfigurationPaths * (EnvironmentReadLimits.MaxPathBytes + 1));

    internal Task<bool> GetNeedDaemonReloadAsync() => GetBooleanPropertyAsync("NeedDaemonReload");

    internal Task<bool> GetTransientAsync() => GetBooleanPropertyAsync("Transient");

    internal Task<string> GetUnitFileStateAsync() => GetStringPropertyAsync("UnitFileState", 128);

    private Task<string> GetStringPropertyAsync(string property, int maximumBytes) =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage(property),
            static (Message message, object? state) =>
                MessageReader.ReadVariantString(message, (int)state!),
            maximumBytes);

    private Task<string[]> GetStringArrayPropertyAsync(
        string property,
        int maximumCount,
        int maximumItemBytes,
        int maximumTotalBytes) =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage(property),
            static (Message message, object? state) =>
            {
                var limits = (StringArrayReadLimits)state!;
                return MessageReader.ReadVariantStringArray(
                    message,
                    limits.MaximumCount,
                    limits.MaximumItemBytes,
                    limits.MaximumTotalBytes);
            },
            new StringArrayReadLimits(maximumCount, maximumItemBytes, maximumTotalBytes));

    private Task<bool> GetBooleanPropertyAsync(string property) =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage(property),
            static (Message message, object? _) => MessageReader.ReadVariantBoolean(message),
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

internal sealed class Service : DBusObject
{
    internal const string Interface = "org.freedesktop.systemd1.Service";

    internal Service(DBusConnection connection, string destination, ObjectPath path)
        : base(connection, destination, path)
    {
    }

    internal Task<string[]> GetEnvironmentAsync() => GetStringArrayPropertyAsync(
        "Environment",
        EnvironmentReadLimits.MaxAssignments + 1,
        EnvironmentReadLimits.MaxLogicalRecordBytes + 1,
        EnvironmentReadLimits.MaxSourceBytes + 1);
    internal Task<(string, bool)[]> GetEnvironmentFilesAsync() =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage("EnvironmentFiles"),
            static (Message message, object? _) => MessageReader.ReadVariantEnvironmentFiles(
                message,
                EnvironmentReadLimits.MaxSources,
                EnvironmentReadLimits.MaxPathBytes + 1,
                EnvironmentReadLimits.MaxSources * (EnvironmentReadLimits.MaxPathBytes + 1)),
            this);
    internal Task<string[]> GetUnsetEnvironmentAsync() => GetStringArrayPropertyAsync(
        "UnsetEnvironment",
        EnvironmentReadLimits.MaxAssignments,
        EnvironmentReadLimits.MaxLogicalRecordBytes + 1,
        EnvironmentReadLimits.MaxSourceBytes + 1);
    internal Task<string[]> GetPassEnvironmentAsync() => GetStringArrayPropertyAsync(
        "PassEnvironment",
        EnvironmentReadLimits.MaxAssignments,
        EnvironmentReadLimits.MaxLogicalRecordBytes + 1,
        EnvironmentReadLimits.MaxSourceBytes + 1);

    private Task<string[]> GetStringArrayPropertyAsync(
        string property,
        int maximumCount,
        int maximumItemBytes,
        int maximumTotalBytes) =>
        Connection.CallMethodAsync(
            CreateGetPropertyMessage(property),
            static (Message message, object? state) =>
            {
                var limits = (StringArrayReadLimits)state!;
                return MessageReader.ReadVariantStringArray(
                    message,
                    limits.MaximumCount,
                    limits.MaximumItemBytes,
                    limits.MaximumTotalBytes);
            },
            new StringArrayReadLimits(maximumCount, maximumItemBytes, maximumTotalBytes));

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
    private const string BoundExceededMessage = "The D-Bus reply exceeds its fixed bound.";
    private const string InvalidStringMessage = "The D-Bus reply contains an invalid string.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void EnsureSignature(string? actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new DBusUnexpectedValueException("The D-Bus reply signature is incompatible.");
        }
    }

    internal static void EnsureVariantSignature(string? actual, string expected) =>
        EnsureSignature(actual, expected);

    internal static string ReadVariantString(Message message, int maximumBytes)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        return ReadVariantString(ref reader, maximumBytes);
    }

    internal static string ReadVariantString(ref Reader reader, int maximumBytes)
    {
        EnsureVariantSignature(reader.ReadSignatureAsString(), "s");
        return ReadString(ref reader, maximumBytes);
    }

    internal static string[] ReadVariantStringArray(
        Message message,
        int maximumCount,
        int maximumItemBytes,
        int maximumTotalBytes)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        return ReadVariantStringArray(
            ref reader,
            maximumCount,
            maximumItemBytes,
            maximumTotalBytes);
    }

    internal static string[] ReadVariantStringArray(
        ref Reader reader,
        int maximumCount,
        int maximumItemBytes,
        int maximumTotalBytes)
    {
        EnsureVariantSignature(reader.ReadSignatureAsString(), "as");
        var entries = new List<string>(Math.Min(maximumCount, 256));
        var totalBytes = 0;
        var arrayEnd = reader.ReadArrayStart(DBusType.String);
        while (reader.HasNext(arrayEnd))
        {
            if (entries.Count == maximumCount)
                throw new DBusReplyLimitException(BoundExceededMessage);
            var bytes = reader.ReadStringAsSpan();
            if (bytes.Length > maximumItemBytes || bytes.Length > maximumTotalBytes - totalBytes)
                throw new DBusReplyLimitException(BoundExceededMessage);
            totalBytes += bytes.Length;
            entries.Add(DecodeString(bytes));
        }

        return entries.ToArray();
    }

    internal static bool ReadVariantBoolean(Message message)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        EnsureVariantSignature(reader.ReadSignatureAsString(), "b");
        return reader.ReadBool();
    }

    internal static (string, bool)[] ReadVariantEnvironmentFiles(
        Message message,
        int maximumCount,
        int maximumPathBytes,
        int maximumTotalBytes)
    {
        EnsureSignature(message.SignatureAsString, "v");
        var reader = message.GetBodyReader();
        return ReadVariantEnvironmentFiles(
            ref reader,
            maximumCount,
            maximumPathBytes,
            maximumTotalBytes);
    }

    internal static (string, bool)[] ReadVariantEnvironmentFiles(
        ref Reader reader,
        int maximumCount,
        int maximumPathBytes,
        int maximumTotalBytes)
    {
        EnsureVariantSignature(reader.ReadSignatureAsString(), "a(sb)");
        var entries = new List<(string, bool)>(Math.Min(maximumCount, 256));
        var totalBytes = 0;
        var arrayEnd = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(arrayEnd))
        {
            if (entries.Count == maximumCount)
                throw new DBusReplyLimitException(BoundExceededMessage);
            reader.AlignStruct();
            var pathBytes = reader.ReadStringAsSpan();
            if (pathBytes.Length > maximumPathBytes || pathBytes.Length > maximumTotalBytes - totalBytes)
                throw new DBusReplyLimitException(BoundExceededMessage);
            totalBytes += pathBytes.Length;
            entries.Add((DecodeString(pathBytes), reader.ReadBool()));
        }
        return entries.ToArray();
    }

    private static string ReadString(ref Reader reader, int maximumBytes)
    {
        var bytes = reader.ReadStringAsSpan();
        if (bytes.Length > maximumBytes)
            throw new DBusReplyLimitException(BoundExceededMessage);
        return DecodeString(bytes);
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new DBusUnexpectedValueException(InvalidStringMessage);
        }
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

internal readonly record struct StringArrayReadLimits(
    int MaximumCount,
    int MaximumItemBytes,
    int MaximumTotalBytes);

internal sealed class DBusReplyLimitException(string message) : Exception(message);
