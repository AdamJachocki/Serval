using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Serval.Web.Listener;

/// <summary>What Web requires of a root-owned configuration file.</summary>
internal enum ProtectedFileKind
{
    /// <summary>Not writable by group or others; readable content is not secret.</summary>
    Configuration,

    /// <summary>A public certificate: group root or serval-web, not writable by group or others.</summary>
    Certificate,

    /// <summary>As Certificate, and a private key: no access at all for others.</summary>
    PrivateKey,
}

internal readonly record struct ProtectedFileFacts(bool IsRegularFile, uint OwnerUid, uint GroupId, ushort Mode);

/// <summary>
/// Opens a file without following any symbolic link and checks ownership and mode on the opened
/// descriptor, so the checked object is the object that is read.
/// </summary>
internal static partial class ProtectedFile
{
    // The openat2/statx plumbing below mirrors src/Serval.Agent/AgentConfiguration.cs (Web must not reference
    // Agent); keep the two in step when changing struct layouts or constants.
    private const int NoSuchFile = 2;
    private const int InvalidArgument = 22;
    private const int OpenReadOnly = 0;
    private const int OpenCloseOnExec = 0x80000;
    private const int AtEmptyPath = 0x1000;
    private const int OpenPathOnly = 0x200000;
    private const ulong ResolveNoMagicLinks = 0x02;
    private const ulong ResolveNoSymlinks = 0x04;
    private const ulong ResolveBeneath = 0x08;
    private const long OpenAt2SystemCall = 437;
    private const uint StatxBasicStats = 0x07ff;
    private const ushort TypeMask = 0xf000;
    private const ushort RegularFileType = 0x8000;
    private const ushort GroupWrite = 0x10;
    private const ushort OtherWrite = 0x02;
    private const ushort OtherAccess = 0x07;

    /// <summary>Pure policy: root-owned, group-owned by root or the expected group, no group/other write.</summary>
    public static bool IsAcceptable(
        ProtectedFileFacts facts, ProtectedFileKind kind, uint expectedGroupId, uint expectedOwnerUid = 0) =>
        facts.IsRegularFile &&
        facts.OwnerUid == expectedOwnerUid &&
        (facts.GroupId == expectedGroupId || (kind != ProtectedFileKind.Configuration && facts.GroupId == 0)) &&
        (facts.Mode & (GroupWrite | OtherWrite)) == 0 &&
        (kind != ProtectedFileKind.PrivateKey || (facts.Mode & OtherAccess) == 0);

    public static uint CurrentGroupId() => Getegid();

    public static uint CurrentUserId() => Geteuid();

    /// <summary>Returns null when the file does not exist; throws for every other problem.</summary>
    public static byte[]? ReadIfPresent(string path, ProtectedFileKind kind, int maximumBytes) =>
        ReadIfPresent(path, kind, maximumBytes, expectedOwnerUid: 0, expectedGroupId: Getegid());

    internal static byte[]? ReadIfPresent(
        string path, ProtectedFileKind kind, int maximumBytes, uint expectedOwnerUid, uint expectedGroupId)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new WebListenerException("Listener configuration requires Linux.");
        }

        var descriptor = OpenNoFollow(path, out var error);
        if (descriptor < 0)
        {
            if (error == NoSuchFile)
            {
                return null;
            }

            throw new WebListenerException("A listener configuration file is unavailable or unsafe.");
        }

        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        if (Statx(descriptor, string.Empty, AtEmptyPath, StatxBasicStats, out var stat) != 0)
        {
            throw new WebListenerException("A listener configuration file could not be inspected.");
        }

        var facts = new ProtectedFileFacts((stat.Mode & TypeMask) == RegularFileType, stat.UserId, stat.GroupId, (ushort)(stat.Mode & 0x01ff));
        if (!IsAcceptable(facts, kind, expectedGroupId, expectedOwnerUid) ||
            stat.Size is 0 || stat.Size > (ulong)maximumBytes)
        {
            throw new WebListenerException("A listener configuration file has unsafe ownership, mode or size.");
        }

        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1);
        var content = new byte[(int)stat.Size];
        stream.ReadExactly(content);
        return content;
    }

    private static int OpenNoFollow(string path, out int error)
    {
        if (path.Length < 2 || path[0] != '/')
        {
            error = InvalidArgument;
            return -1;
        }

        var rootDescriptor = Open("/", OpenPathOnly | OpenCloseOnExec, 0);
        if (rootDescriptor < 0)
        {
            error = Marshal.GetLastPInvokeError();
            return -1;
        }

        using var root = new SafeFileHandle(rootDescriptor, ownsHandle: true);
        var how = new OpenHow
        {
            Flags = OpenReadOnly | OpenCloseOnExec,
            Resolve = ResolveBeneath | ResolveNoMagicLinks | ResolveNoSymlinks,
        };
        var descriptor = (int)SyscallOpenAt2(OpenAt2SystemCall, rootDescriptor, path[1..], ref how, (nuint)Marshal.SizeOf<OpenHow>());
        error = descriptor < 0 ? Marshal.GetLastPInvokeError() : 0;
        return descriptor;
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint Geteuid();

    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint Getegid();

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial long SyscallOpenAt2(long number, int directoryDescriptor, string path, ref OpenHow how, nuint size);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directoryDescriptor, string path, int flags, uint mask, out StatxBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenHow
    {
        internal ulong Flags;
        internal ulong Mode;
        internal ulong Resolve;
    }

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
