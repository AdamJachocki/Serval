using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Serval.Agent;

/// <summary>Root-owned deployment values; paths and PAM service are fixed by the binary.</summary>
public sealed partial record AgentConfiguration(
    uint WebUid, uint WebGid, string AdminGroup, uint AdminGid, TimeSpan IdleTimeout)
{
    public const string ConfigurationPath = "/etc/serval/agent.json";
    public const string SocketPath = "/run/serval/agent.sock";
    public const string PolicyPath = "/var/lib/serval/policy.db";
    public const string PamService = "serval";

    private const string InvalidConfiguration = "Agent configuration is invalid.";

    private const int OpenReadOnly = 0;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenNoFollow = 0x20000;
    private const int OpenDirectory = 0x10000;
    private const int AtEmptyPath = 0x1000;
    private const int NoSuchFile = 2;
    private const uint StatxBasicStats = 0x07ff;
    private const ushort TypeMask = 0xf000;
    private const ushort RegularFile = 0x8000;
    private const ushort DirectoryType = 0x4000;
    private const ushort GroupOrOtherWrite = 0x12;

    public static AgentConfiguration Load() => LoadFromPaths("/etc/serval", ConfigurationPath);

    internal static AgentConfiguration LoadFromPaths(string directoryPath, string filePath)
    {
        if (!OperatingSystem.IsLinux() || Geteuid() != 0)
        {
            throw new InvalidOperationException("Agent requires Linux root identity.");
        }

        using var directory = OpenSafe(directoryPath, OpenDirectory, DirectoryType);
        using var file = OpenSafe(filePath, 0, RegularFile);
        using var stream = new FileStream(file, FileAccess.Read);
        if (stream.Length is <= 0 or > 4096)
        {
            throw new InvalidOperationException("Agent configuration size is invalid.");
        }

        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 2,
        });
        return Parse(document.RootElement);
    }

    public void ValidateRuntimeDirectory() => ValidateRuntimeDirectory("/run/serval");

    internal void ValidateRuntimeDirectory(string path)
    {
        using var directory = OpenSafe(path, OpenDirectory, DirectoryType);
        if (Statx(directory.DangerousGetHandle().ToInt32(), string.Empty,
                AtEmptyPath, StatxBasicStats, out var stat) != 0 ||
            stat.GroupId != WebGid ||
            (stat.Mode & 0x01ff) != 0x01e8)
        {
            throw new InvalidOperationException("Agent socket directory is unsafe.");
        }
    }

    public static void ValidatePolicyLocation() =>
        ValidatePolicyLocation("/var/lib/serval", PolicyPath);

    internal static void ValidatePolicyLocation(string directoryPath, string policyPath)
    {
        using var directory = OpenSafe(directoryPath, OpenDirectory, DirectoryType);
        if (Statx(directory.DangerousGetHandle().ToInt32(), string.Empty,
                AtEmptyPath, StatxBasicStats, out var stat) != 0 ||
            (stat.Mode & 0x01ff) != 0x01c0)
        {
            throw new InvalidOperationException("Agent policy directory is unsafe.");
        }

        var descriptor = Open(policyPath, OpenReadOnly | OpenCloseOnExec | OpenNoFollow, 0);
        if (descriptor < 0)
        {
            if (Marshal.GetLastPInvokeError() == NoSuchFile)
            {
                return;
            }

            throw new InvalidOperationException("Agent policy path is unsafe.");
        }

        using var file = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        if (Statx(descriptor, string.Empty, AtEmptyPath, StatxBasicStats, out stat) != 0 ||
            stat.UserId != 0 || (stat.Mode & TypeMask) != RegularFile ||
            (stat.Mode & 0x01ff) != 0x0180)
        {
            throw new InvalidOperationException("Agent policy file is unsafe.");
        }
    }

    public static void ValidatePamService() => ValidatePamService("/etc/pam.d/serval");

    internal static void ValidatePamService(string path)
    {
        using var file = OpenSafe(path, 0, RegularFile);
    }

    public static AgentConfiguration Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!fields.TryAdd(property.Name, property.Value))
            {
                throw new InvalidOperationException(InvalidConfiguration);
            }
        }

        if (fields.Keys.Any(key => key is not ("webUid" or "webGid" or "adminGroup" or "adminGid" or "idleMinutes")) ||
            fields.Count is < 4 or > 5)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var webUid = RequiredPositiveUInt(fields, "webUid");
        var webGid = RequiredPositiveUInt(fields, "webGid");
        var adminGid = RequiredPositiveUInt(fields, "adminGid");
        if (!fields.TryGetValue("adminGroup", out var group) || group.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var adminGroup = group.GetString()!;
        if (adminGroup.Length is < 1 or > 64 ||
            !char.IsAsciiLetterLower(adminGroup[0]) ||
            adminGroup.Skip(1).Any(value => !char.IsAsciiLetterOrDigit(value) && value is not ('_' or '-')))
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        var idleMinutes = fields.TryGetValue("idleMinutes", out var interval)
            ? interval.ValueKind == JsonValueKind.Number && interval.TryGetInt32(out var value) ? value : 0
            : 15;
        if (idleMinutes is < 1 or > 120)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        return new AgentConfiguration(webUid, webGid, adminGroup, adminGid,
            TimeSpan.FromMinutes(idleMinutes));
    }

    private static uint RequiredPositiveUInt(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetUInt32(out var number) || number == 0)
        {
            throw new InvalidOperationException(InvalidConfiguration);
        }

        return number;
    }

    private static SafeFileHandle OpenSafe(string path, int extraFlags, ushort expectedType)
    {
        var descriptor = Open(path, OpenReadOnly | OpenCloseOnExec | OpenNoFollow | extraFlags, 0);
        if (descriptor < 0)
        {
            throw new InvalidOperationException("Agent configuration path is unavailable.");
        }

        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        if (Statx(descriptor, string.Empty, AtEmptyPath, StatxBasicStats, out var stat) != 0 ||
            stat.UserId != 0 || (stat.Mode & TypeMask) != expectedType ||
            (stat.Mode & GroupOrOtherWrite) != 0)
        {
            handle.Dispose();
            throw new InvalidOperationException("Agent configuration path is unsafe.");
        }

        return handle;
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint Geteuid();

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directoryDescriptor, string path, int flags, uint mask, out StatxBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct StatxTimestamp
    {
        internal long Seconds;
        internal uint Nanoseconds;
        internal int Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct StatxBuffer
    {
        internal uint Mask;
        internal uint BlockSize;
        internal ulong Attributes;
        internal uint LinkCount;
        internal uint UserId;
        internal uint GroupId;
        internal ushort Mode;
        internal ushort Spare;
        internal ulong Inode;
        internal ulong Size;
        internal ulong Blocks;
        internal ulong AttributesMask;
        internal StatxTimestamp Accessed;
        internal StatxTimestamp Created;
        internal StatxTimestamp Changed;
        internal StatxTimestamp Modified;
        internal uint RDeviceMajor;
        internal uint RDeviceMinor;
        internal uint DeviceMajor;
        internal uint DeviceMinor;
    }
}
