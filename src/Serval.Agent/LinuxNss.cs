using System.Runtime.InteropServices;

namespace Serval.Agent;

public sealed record LinuxGroup(string Name, uint Gid);

public sealed record LinuxAccount(
    string Name, uint Uid, uint PrimaryGid, IReadOnlyList<LinuxGroup> Groups,
    string? Home = null, string? Shell = null, bool PasswordLocked = false);

/// <summary>Bounded-buffer NSS lookups. Call only through the capped native worker pool.</summary>
public static partial class LinuxNss
{
    private const int RangeError = 34;
    private const int MaximumBuffer = 64 * 1024;
    private const int MaximumGroups = 1024;

    public static LinuxAccount? ResolveAccount(string name)
    {
        if (!OperatingSystem.IsLinux() || !ValidName(name))
        {
            return null;
        }

        var password = LookupPasswd(name);
        if (password is null)
        {
            return null;
        }

        var groups = new uint[MaximumGroups];
        var count = groups.Length;
        if (GetGroupList(name, password.Value.Gid, groups, ref count) < 0 ||
            count is < 1 or > MaximumGroups)
        {
            return null;
        }

        var resolved = new List<LinuxGroup>(count);
        foreach (var gid in groups.Take(count).Distinct())
        {
            var group = LookupGroup(gid);
            if (group is null)
            {
                return null;
            }

            resolved.Add(group);
        }

        if (resolved.All(group => group.Gid != password.Value.Gid))
        {
            return null;
        }

        return new LinuxAccount(name, password.Value.Uid, password.Value.Gid,
            Array.AsReadOnly(resolved.ToArray()));
    }

    public static LinuxGroup? ResolveGroup(string name)
    {
        if (!OperatingSystem.IsLinux() || !ValidName(name))
        {
            return null;
        }

        for (var size = 4096; size <= MaximumBuffer; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetGroupByName(name, out var group, buffer, (nuint)size, out var result);
                if (status == RangeError)
                {
                    continue;
                }

                if (status != 0 || result == IntPtr.Zero ||
                    !string.Equals(Marshal.PtrToStringUTF8(group.Name), name, StringComparison.Ordinal))
                {
                    return null;
                }

                return new LinuxGroup(name, group.Gid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    public static LinuxAccount? ResolveAccountByUid(uint uid)
    {
        if (!OperatingSystem.IsLinux() || uid == 0)
        {
            return null;
        }

        for (var size = 4096; size <= MaximumBuffer; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetPasswordById(uid, out var password, buffer,
                    (nuint)size, out var result);
                if (status == RangeError)
                {
                    continue;
                }

                var name = status == 0 && result != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(password.Name)
                    : null;
                if (!ValidName(name))
                {
                    return null;
                }

                var account = ResolveAccount(name!);
                return account is not null && account.Uid == uid &&
                    account.PrimaryGid == password.Gid
                    ? account with
                    {
                        Home = Marshal.PtrToStringUTF8(password.Home),
                        Shell = Marshal.PtrToStringUTF8(password.Shell),
                        PasswordLocked = IsPasswordLocked(name!),
                    }
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    private static (uint Uid, uint Gid)? LookupPasswd(string name)
    {
        for (var size = 4096; size <= MaximumBuffer; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetPasswordByName(name, out var password, buffer, (nuint)size,
                    out var result);
                if (status == RangeError)
                {
                    continue;
                }

                if (status != 0 || result == IntPtr.Zero ||
                    !string.Equals(Marshal.PtrToStringUTF8(password.Name), name, StringComparison.Ordinal))
                {
                    return null;
                }

                return (password.Uid, password.Gid);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    private static LinuxGroup? LookupGroup(uint gid)
    {
        for (var size = 4096; size <= MaximumBuffer; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetGroupById(gid, out var group, buffer, (nuint)size, out var result);
                if (status == RangeError)
                {
                    continue;
                }

                var name = status == 0 && result != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(group.Name)
                    : null;
                return name is not null && ValidName(name)
                    ? new LinuxGroup(name, gid)
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    private static bool ValidName(string? name) =>
        name is { Length: > 0 and <= 128 } &&
        !name.Any(value => value is '\0' or '/' or '\r' or '\n' || char.IsControl(value));

    private static unsafe bool IsPasswordLocked(string name)
    {
        for (var size = 4096; size <= MaximumBuffer; size *= 2)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = GetShadowByName(name, out var shadow, buffer, (nuint)size,
                    out var result);
                if (status == RangeError)
                {
                    continue;
                }

                return status == 0 && result != IntPtr.Zero &&
                    string.Equals(Marshal.PtrToStringUTF8(shadow.Name), name,
                        StringComparison.Ordinal) &&
                    shadow.Password != IntPtr.Zero &&
                    Marshal.ReadByte(shadow.Password) is (byte)'!' or (byte)'*';
            }
            finally
            {
                new Span<byte>((void*)buffer, size).Clear();
                Marshal.FreeHGlobal(buffer);
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Passwd
    {
        internal IntPtr Name;
        internal IntPtr Password;
        internal uint Uid;
        internal uint Gid;
        internal IntPtr Gecos;
        internal IntPtr Home;
        internal IntPtr Shell;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Group
    {
        internal IntPtr Name;
        internal IntPtr Password;
        internal uint Gid;
        internal IntPtr Members;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Shadow
    {
        internal IntPtr Name;
        internal IntPtr Password;
        internal long LastChange;
        internal long Minimum;
        internal long Maximum;
        internal long Warning;
        internal long Inactive;
        internal long Expire;
        internal ulong Flags;
    }

    [LibraryImport("libc", EntryPoint = "getpwnam_r", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GetPasswordByName(
        string name, out Passwd password, IntPtr buffer, nuint length, out IntPtr result);

    [LibraryImport("libc", EntryPoint = "getpwuid_r")]
    private static partial int GetPasswordById(
        uint uid, out Passwd password, IntPtr buffer, nuint length, out IntPtr result);

    [LibraryImport("libc", EntryPoint = "getgrnam_r", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GetGroupByName(
        string name, out Group group, IntPtr buffer, nuint length, out IntPtr result);

    [LibraryImport("libc", EntryPoint = "getgrgid_r")]
    private static partial int GetGroupById(
        uint gid, out Group group, IntPtr buffer, nuint length, out IntPtr result);

    [LibraryImport("libc", EntryPoint = "getspnam_r", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GetShadowByName(
        string name, out Shadow shadow, IntPtr buffer, nuint length, out IntPtr result);

    [LibraryImport("libc", EntryPoint = "getgrouplist", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GetGroupList(string name, uint primaryGid, uint[] groups, ref int count);
}
