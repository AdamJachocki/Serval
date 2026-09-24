using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Serval.Systemd;

internal interface ISystemdSourceFileAccess
{
    ValueTask<SystemdFileObservation> ObserveAsync(
        string path,
        bool readContent,
        bool allowMissing,
        CancellationToken cancellationToken,
        int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes);

    ValueTask<bool> IsStableAsync(SystemdFileObservation observation, CancellationToken cancellationToken);
}

internal enum SystemdSourceFileFailure
{
    Missing,
    Unavailable,
    UnsafePath,
    Inconsistent,
    LimitExceeded,
}

internal sealed class SystemdSourceFileException(SystemdSourceFileFailure failure) : Exception
{
    internal SystemdSourceFileFailure Failure { get; } = failure;
    public override string Message => "The systemd source file operation failed.";
    public override string ToString() => Message;
}

internal sealed class SystemdFileObservation : IDisposable
{
    private ClearableBytes? _content;

    internal SystemdFileObservation(string path, bool missing)
    {
        Path = path;
        IsMissing = missing;
    }

    internal SystemdFileObservation(string path, ClearableBytes content)
    {
        Path = path;
        _content = content;
    }

    internal SystemdFileObservation(
        string path,
        SafeFileHandle handle,
        LinuxFileIdentity identity,
        ClearableBytes? content)
    {
        Path = path;
        Handle = handle;
        Identity = identity;
        _content = content;
    }

    internal string Path { get; }
    internal bool IsMissing { get; }
    internal SafeFileHandle? Handle { get; }
    internal LinuxFileIdentity Identity { get; }
    internal int ContentLength => _content?.Length ?? 0;

    internal ClearableBytes TakeContent()
    {
        var content = _content ?? throw new InvalidOperationException("The observation has no content.");
        _content = null;
        return content;
    }

    public void Dispose()
    {
        _content?.Dispose();
        Handle?.Dispose();
    }
}

internal readonly record struct LinuxFileIdentity(
    ulong Device,
    ulong Inode,
    ulong Size,
    ushort Mode,
    long ModifiedSeconds,
    uint ModifiedNanoseconds,
    long ChangedSeconds,
    uint ChangedNanoseconds);

internal delegate ValueTask<int> LinuxFileReadAsync(
    SafeFileHandle handle,
    Memory<byte> buffer,
    long offset,
    CancellationToken cancellationToken);

internal delegate SafeFileHandle LinuxFileOpen(
    string path,
    bool readContent,
    CancellationToken cancellationToken);

internal delegate int LinuxFileStatx(
    SafeFileHandle handle,
    out LinuxSystemdSourceFileAccess.StatxBuffer buffer);

internal sealed partial class LinuxSystemdSourceFileAccess : ISystemdSourceFileAccess
{
    private const int AtFdcwd = -100;
    private const int AtEmptyPath = 0x1000;
    private const int OReadOnly = 0;
    private const int ONonBlock = 0x800;
    private const int OCloseOnExec = 0x80000;
    private const int OPath = 0x200000;
    private const ulong ResolveNoMagicLinks = 0x02;
    private const ulong ResolveNoSymlinks = 0x04;
    private const ulong ResolveBeneath = 0x08;
    private const long OpenAt2SystemCall = 437;
    private const uint StatxBasicStats = 0x07ff;
    private const uint StatxRequiredIdentity = 0x03c3;
    private const ushort FileTypeMask = 0xf000;
    private const ushort RegularFile = 0x8000;
    private const int NotFound = 2;
    private const int AccessDenied = 13;
    private const int NoSuchDeviceOrAddress = 6;
    private const int NotDirectory = 20;
    private const int TooManyLinks = 40;
    private const int CrossDevice = 18;
    private const long ProcSuperMagic = 0x9fa0;
    private const long SysfsMagic = 0x62656572;
    private const long CgroupMagic = 0x27e0eb;
    private const long Cgroup2Magic = 0x63677270;
    private const long DebugfsMagic = 0x64626720;
    private const long TracefsMagic = 0x74726163;
    private const long SecurityfsMagic = 0x73636673;
    private const long SelinuxMagic = 0xf97cff8c;
    private const long SmackMagic = 0x43415d53;
    private const long ConfigfsMagic = 0x62656570;
    private const long PstorefsMagic = 0x6165676c;
    private const long EfivarfsMagic = 0xde5e81e4;
    private const long HugetlbfsMagic = 0x958458f6;
    private const long OpenpromMagic = 0x9fa1;
    private const long XenfsMagic = 0xabba1974;
    private const long RdtgroupMagic = 0x7655821;
    private const long BdevfsMagic = 0x62646576;
    private const long DaxfsMagic = 0x64646178;
    private const long BinfmtfsMagic = 0x42494e4d;
    private const long DevptsMagic = 0x1cd1;
    private const long BinderfsMagic = 0x6c6f6f70;
    private const long FutexfsMagic = 0x0bad1dea;
    private const long PipefsMagic = 0x50495045;
    private const long SockfsMagic = 0x534f434b;
    private const long UsbdeviceMagic = 0x9fa2;
    private const long MtdInodeFsMagic = 0x11307854;
    private const long AnonInodeFsMagic = 0x09041934;
    private const long BtrfsTestMagic = 0x73727279;
    private const long NsfsMagic = 0x6e736673;
    private const long BpfFsMagic = 0xcafe4a11;
    private const long AafsMagic = 0x5a3c69f0;
    private const long ZonefsMagic = 0x5a4f4653;
    private const long DmaBufMagic = 0x444d4142;
    private const long DevmemMagic = 0x454d444d;
    private const long SecretmemMagic = 0x5345434d;
    private const long MqueueMagic = 0x19800202;
    private const long RpcPipefsMagic = 0x67596969;
    private const long AutofsMagic = 0x0187;
    private readonly LinuxFileReadAsync _readAsync;
    private readonly LinuxFileOpen _openSafely;
    private readonly LinuxFileStatx _statx;

    internal LinuxSystemdSourceFileAccess()
        : this(
            static (handle, buffer, offset, token) =>
                RandomAccess.ReadAsync(handle, buffer, offset, token),
            OpenSafely,
            ReadStatx)
    {
    }

    internal LinuxSystemdSourceFileAccess(LinuxFileReadAsync readAsync)
        : this(readAsync, OpenSafely, ReadStatx)
    {
    }

    internal LinuxSystemdSourceFileAccess(
        LinuxFileReadAsync readAsync,
        LinuxFileOpen openSafely)
        : this(readAsync, openSafely, ReadStatx)
    {
    }

    internal LinuxSystemdSourceFileAccess(
        LinuxFileReadAsync readAsync,
        LinuxFileOpen openSafely,
        LinuxFileStatx statx)
    {
        _readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
        _openSafely = openSafely ?? throw new ArgumentNullException(nameof(openSafely));
        _statx = statx ?? throw new ArgumentNullException(nameof(statx));
    }

    public async ValueTask<SystemdFileObservation> ObserveAsync(
        string path,
        bool readContent,
        bool allowMissing,
        CancellationToken cancellationToken,
        int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumContentBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            maximumContentBytes,
            EnvironmentReadLimits.MaxSourceBytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Safe systemd source access requires Linux.");

        SafeFileHandle? handle = null;
        ClearableBytes? content = null;
        try
        {
            handle = _openSafely(path, readContent, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var before = ReadIdentity(handle, cancellationToken);
            EnsureSafeFile(handle, before, cancellationToken);
            if (readContent)
            {
                content = new ClearableBytes();
                await ReadContentAsync(handle, content, maximumContentBytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            var after = ReadIdentity(handle, cancellationToken);
            if (before != after)
                throw new SystemdSourceFileException(SystemdSourceFileFailure.Inconsistent);
            var observation = new SystemdFileObservation(path, handle, before, content);
            handle = null;
            content = null;
            return observation;
        }
        catch (SystemdSourceFileException exception) when (
            allowMissing && exception.Failure == SystemdSourceFileFailure.Missing)
        {
            return new SystemdFileObservation(path, missing: true);
        }
        finally
        {
            content?.Dispose();
            handle?.Dispose();
        }
    }

    public ValueTask<bool> IsStableAsync(
        SystemdFileObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();
        SafeFileHandle? rebound = null;
        try
        {
            if (observation.IsMissing)
            {
                try
                {
                    rebound = _openSafely(observation.Path, readContent: false, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(false);
                }
                catch (SystemdSourceFileException exception) when (
                    exception.Failure == SystemdSourceFileFailure.Missing)
                {
                    return ValueTask.FromResult(true);
                }
            }

            if (observation.Handle is null || observation.Handle.IsInvalid || observation.Handle.IsClosed)
                return ValueTask.FromResult(false);
            if (ReadIdentity(observation.Handle, cancellationToken) != observation.Identity)
                return ValueTask.FromResult(false);
            rebound = _openSafely(observation.Path, readContent: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeFile(rebound, ReadIdentity(rebound, cancellationToken), cancellationToken);
            return ValueTask.FromResult(ReadIdentity(rebound, cancellationToken) == observation.Identity);
        }
        catch (SystemdSourceFileException)
        {
            return ValueTask.FromResult(false);
        }
        finally
        {
            rebound?.Dispose();
        }
    }

    private static SafeFileHandle OpenSafely(
        string path,
        bool readContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rootDescriptor = Open("/", OPath | OCloseOnExec, 0);
        using var root = new SafeFileHandle(rootDescriptor, ownsHandle: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (root.IsInvalid)
            throw MapLastError();

        var how = new OpenHow
        {
            Flags = (ulong)((readContent ? OReadOnly | ONonBlock : OPath) | OCloseOnExec),
            Resolve = ResolveBeneath | ResolveNoMagicLinks | ResolveNoSymlinks,
        };
        var relativePath = path[1..];
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = SyscallOpenAt2(
            OpenAt2SystemCall,
            root.DangerousGetHandle().ToInt32(),
            relativePath,
            ref how,
            (nuint)Marshal.SizeOf<OpenHow>());
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handle.IsInvalid)
                throw MapLastError();
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private async Task ReadContentAsync(
        SafeFileHandle handle,
        ClearableBytes content,
        int maximumContentBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            long offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requestBytes = Math.Min(
                    buffer.Length,
                    maximumContentBytes - content.Length + 1);
                var read = await _readAsync(
                        handle,
                        buffer.AsMemory(0, requestBytes),
                        offset,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (read == 0)
                    return;
                if (read > maximumContentBytes - content.Length)
                    throw new SystemdSourceFileException(SystemdSourceFileFailure.LimitExceeded);
                content.Append(buffer.AsSpan(0, read));
                offset += read;
            }
        }
        catch (SystemdSourceFileException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SystemdSourceFileException(SystemdSourceFileFailure.Unavailable);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private LinuxFileIdentity ReadIdentity(
        SafeFileHandle handle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _statx(handle, out var stat);
        cancellationToken.ThrowIfCancellationRequested();
        if (result != 0 || (stat.Mask & StatxRequiredIdentity) != StatxRequiredIdentity)
            throw new SystemdSourceFileException(SystemdSourceFileFailure.Unavailable);
        var device = ((ulong)stat.DeviceMajor << 32) | stat.DeviceMinor;
        return new LinuxFileIdentity(
            device,
            stat.Inode,
            stat.Size,
            stat.Mode,
            stat.Modified.Seconds,
            stat.Modified.Nanoseconds,
            stat.Changed.Seconds,
            stat.Changed.Nanoseconds);
    }

    private static int ReadStatx(SafeFileHandle handle, out StatxBuffer buffer) =>
        Statx(
            handle.DangerousGetHandle().ToInt32(),
            string.Empty,
            AtEmptyPath,
            StatxBasicStats,
            out buffer);

    private static void EnsureSafeFile(
        SafeFileHandle handle,
        LinuxFileIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((identity.Mode & FileTypeMask) != RegularFile)
            throw new SystemdSourceFileException(SystemdSourceFileFailure.UnsafePath);
        cancellationToken.ThrowIfCancellationRequested();
        var result = Fstatfs(handle.DangerousGetHandle().ToInt32(), out var fileSystem);
        cancellationToken.ThrowIfCancellationRequested();
        if (result != 0)
            throw new SystemdSourceFileException(SystemdSourceFileFailure.Unavailable);
        if (IsUnsafeFileSystemType(fileSystem.Type))
            throw new SystemdSourceFileException(SystemdSourceFileFailure.UnsafePath);
    }

    internal static bool IsUnsafeFileSystemType(long type) => type is
        ProcSuperMagic or SysfsMagic or CgroupMagic or Cgroup2Magic or
        DebugfsMagic or TracefsMagic or SecurityfsMagic or SelinuxMagic or SmackMagic or
        ConfigfsMagic or PstorefsMagic or EfivarfsMagic or HugetlbfsMagic or
        OpenpromMagic or XenfsMagic or RdtgroupMagic or BdevfsMagic or DaxfsMagic or
        BinfmtfsMagic or DevptsMagic or BinderfsMagic or FutexfsMagic or PipefsMagic or
        SockfsMagic or UsbdeviceMagic or MtdInodeFsMagic or AnonInodeFsMagic or
        BtrfsTestMagic or NsfsMagic or BpfFsMagic or AafsMagic or ZonefsMagic or
        DmaBufMagic or DevmemMagic or SecretmemMagic or MqueueMagic or RpcPipefsMagic or
        AutofsMagic;

    private static SystemdSourceFileException MapLastError() => Marshal.GetLastPInvokeError() switch
    {
        NotFound => new(SystemdSourceFileFailure.Missing),
        NotDirectory or TooManyLinks or CrossDevice or NoSuchDeviceOrAddress =>
            new(SystemdSourceFileFailure.UnsafePath),
        AccessDenied => new(SystemdSourceFileFailure.Unavailable),
        _ => new(SystemdSourceFileFailure.Unavailable),
    };

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags, int mode);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial long SyscallOpenAt2(
        long number,
        int directoryDescriptor,
        string path,
        ref OpenHow how,
        nuint size);

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(
        int directoryDescriptor,
        string path,
        int flags,
        uint mask,
        out StatxBuffer buffer);

    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int Fstatfs(int descriptor, out StatfsBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenHow
    {
        internal ulong Flags;
        internal ulong Mode;
        internal ulong Resolve;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StatxTimestamp
    {
        internal long Seconds;
        internal uint Nanoseconds;
        internal int Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    internal struct StatxBuffer
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

    [StructLayout(LayoutKind.Sequential)]
    private struct StatfsBuffer
    {
        internal long Type;
        internal long BlockSize;
        internal ulong Blocks;
        internal ulong BlocksFree;
        internal ulong BlocksAvailable;
        internal ulong Files;
        internal ulong FilesFree;
        internal int FileSystemId1;
        internal int FileSystemId2;
        internal long NameLength;
        internal long FragmentSize;
        internal long Flags;
        internal long Spare1;
        internal long Spare2;
        internal long Spare3;
        internal long Spare4;
    }
}
