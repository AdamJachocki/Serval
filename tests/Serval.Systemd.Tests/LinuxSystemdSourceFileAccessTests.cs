using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class LinuxSystemdSourceFileAccessTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact]
    public void StatxBufferMatchesLinuxDeviceFieldOffsets()
    {
        Assert.Equal(256, Marshal.SizeOf<LinuxSystemdSourceFileAccess.StatxBuffer>());
        Assert.Equal(128, Marshal.OffsetOf<LinuxSystemdSourceFileAccess.StatxBuffer>(
            nameof(LinuxSystemdSourceFileAccess.StatxBuffer.RDeviceMajor)).ToInt32());
        Assert.Equal(132, Marshal.OffsetOf<LinuxSystemdSourceFileAccess.StatxBuffer>(
            nameof(LinuxSystemdSourceFileAccess.StatxBuffer.RDeviceMinor)).ToInt32());
        Assert.Equal(136, Marshal.OffsetOf<LinuxSystemdSourceFileAccess.StatxBuffer>(
            nameof(LinuxSystemdSourceFileAccess.StatxBuffer.DeviceMajor)).ToInt32());
        Assert.Equal(140, Marshal.OffsetOf<LinuxSystemdSourceFileAccess.StatxBuffer>(
            nameof(LinuxSystemdSourceFileAccess.StatxBuffer.DeviceMinor)).ToInt32());
    }

    [Fact(Skip = "Requires Linux file descriptor semantics.", SkipUnless = nameof(IsLinux))]
    public async Task RejectsSuccessfulStatxReplyMissingRequiredIdentityFields()
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("partial-statx.env");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        var statxCalls = 0;
        var access = new LinuxSystemdSourceFileAccess(
            static (handle, buffer, offset, token) =>
                RandomAccess.ReadAsync(handle, buffer, offset, token),
            static (requestedPath, _, _) => File.OpenHandle(
                requestedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess),
            (SafeFileHandle _, out LinuxSystemdSourceFileAccess.StatxBuffer buffer) =>
            {
                statxCalls++;
                buffer = new LinuxSystemdSourceFileAccess.StatxBuffer
                {
                    Mask = 0x0003,
                    Mode = 0x8000,
                };
                return 0;
            });

        var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
            await access.ObserveAsync(
                path,
                readContent: false,
                allowMissing: false,
                TestContext.Current.CancellationToken));

        Assert.Equal(SystemdSourceFileFailure.Unavailable, exception.Failure);
        Assert.Equal(1, statxCalls);
    }

    [Fact]
    public void RejectsKernelVirtualFilesystemsWhileAllowingOrdinaryAndTmpfsTypes()
    {
        long[] unsafeTypes =
        [
            0x62656570, // configfs
            0xde5e81e4, // efivarfs
            0x6165676c, // pstorefs
            0xcafe4a11, // bpffs
            0x6e736673, // nsfs
            0x19800202, // mqueue
            0x67596969, // rpc_pipefs
        ];

        Assert.All(unsafeTypes, type => Assert.True(
            LinuxSystemdSourceFileAccess.IsUnsafeFileSystemType(type)));
        Assert.False(LinuxSystemdSourceFileAccess.IsUnsafeFileSystemType(0x01021994)); // tmpfs
        Assert.False(LinuxSystemdSourceFileAccess.IsUnsafeFileSystemType(0xef53)); // ext2/3/4
        Assert.False(LinuxSystemdSourceFileAccess.IsUnsafeFileSystemType(0x65735546)); // FUSE
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task ReadsRegularFileWithoutChangingItAndRevalidatesBinding()
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("source.env");
        await File.WriteAllBytesAsync(path, [1, 2, 3], TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var access = new LinuxSystemdSourceFileAccess();

        using var observation = await access.ObserveAsync(
            path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken);

        Assert.Equal(before.Length, observation.ContentLength);
        Assert.True(await access.IsStableAsync(observation, TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task RejectsIntermediateAndFinalSymlinksAndProcessPseudoFiles()
    {
        using var fixture = new FileFixture();
        var target = fixture.Path("target.env");
        await File.WriteAllBytesAsync(target, [1], TestContext.Current.CancellationToken);
        var finalLink = fixture.Path("final.env");
        File.CreateSymbolicLink(finalLink, target);
        var directory = fixture.Path("directory");
        Directory.CreateDirectory(directory);
        var directoryLink = fixture.Path("directory-link");
        Directory.CreateSymbolicLink(directoryLink, directory);
        var access = new LinuxSystemdSourceFileAccess();

        await Unsafe(access, finalLink);
        await Unsafe(access, Path.Combine(directoryLink, "missing.env"));
        await Unsafe(access, "/proc/self/status");
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task RejectsFifoSocketDeviceAndOversizedSourceWithoutBlocking()
    {
        using var fixture = new FileFixture();
        var fifo = fixture.Path("source.fifo");
        CreateFifo(fifo);
        var socketPath = fixture.Path("source.socket");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath));
        var oversized = fixture.Path("oversized.env");
        await using (var stream = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write))
            stream.SetLength(EnvironmentReadLimits.MaxSourceBytes + 1L);
        var access = new LinuxSystemdSourceFileAccess();

        await Unsafe(access, fifo);
        await Unsafe(access, socketPath);
        await Unsafe(access, "/dev/null");
        var limit = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
            await access.ObserveAsync(
                oversized, readContent: true, allowMissing: false, TestContext.Current.CancellationToken));
        Assert.Equal(SystemdSourceFileFailure.LimitExceeded, limit.Failure);
    }

    [Fact(Skip = "Requires a Linux POSIX message-queue filesystem.", SkipUnless = nameof(IsLinux))]
    public async Task RejectsRegularLookingMessageQueueNodeBeforeReading()
    {
        if (!Directory.Exists("/dev/mqueue"))
            return;

        var library = NativeLibrary.Load("libc.so.6");
        var queueName = "/serval-source-test-" + Guid.NewGuid().ToString("N");
        var descriptor = -1;
        try
        {
            var open = Marshal.GetDelegateForFunctionPointer<MqOpen>(
                NativeLibrary.GetExport(library, "mq_open"));
            descriptor = open(queueName, 0x40 | 0x80 | 0x2 | 0x80000, 0x180, IntPtr.Zero);
            Assert.True(descriptor >= 0);

            var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
                await new LinuxSystemdSourceFileAccess().ObserveAsync(
                    "/dev/mqueue/" + queueName[1..],
                    readContent: true,
                    allowMissing: false,
                    TestContext.Current.CancellationToken));

            Assert.Equal(SystemdSourceFileFailure.UnsafePath, exception.Failure);
        }
        finally
        {
            if (descriptor >= 0)
                Marshal.GetDelegateForFunctionPointer<MqClose>(
                    NativeLibrary.GetExport(library, "mq_close"))(descriptor);
            Marshal.GetDelegateForFunctionPointer<MqUnlink>(
                NativeLibrary.GetExport(library, "mq_unlink"))(queueName);
            NativeLibrary.Free(library);
        }
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task DetectsReplacementMutationAndContinuedOptionalAbsence()
    {
        if (!OperatingSystem.IsLinux())
            return;
        using var fixture = new FileFixture();
        var path = fixture.Path("source.env");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        var access = new LinuxSystemdSourceFileAccess();
        using var contentObservation = await access.ObserveAsync(
            path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, [2, 3], TestContext.Current.CancellationToken);
        Assert.False(await access.IsStableAsync(contentObservation, TestContext.Current.CancellationToken));

        using var metadataObservation = await access.ObserveAsync(
            path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.False(await access.IsStableAsync(metadataObservation, TestContext.Current.CancellationToken));

        using var replacementObservation = await access.ObserveAsync(
            path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken);
        File.Delete(path);
        await File.WriteAllBytesAsync(path, [4], TestContext.Current.CancellationToken);
        Assert.False(await access.IsStableAsync(replacementObservation, TestContext.Current.CancellationToken));

        var missingPath = fixture.Path("missing.env");
        using var missing = await access.ObserveAsync(
            missingPath, readContent: true, allowMissing: true, TestContext.Current.CancellationToken);
        Assert.True(missing.IsMissing);
        Assert.True(await access.IsStableAsync(missing, TestContext.Current.CancellationToken));
        await File.WriteAllBytesAsync(missingPath, [3], TestContext.Current.CancellationToken);
        Assert.False(await access.IsStableAsync(missing, TestContext.Current.CancellationToken));
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task DetectsContentMutationDuringStreaming()
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("mutating.env");
        await File.WriteAllBytesAsync(
            path,
            new byte[128 * 1024],
            TestContext.Current.CancellationToken);
        var mutated = false;
        var access = new LinuxSystemdSourceFileAccess(async (handle, buffer, offset, token) =>
        {
            var read = await RandomAccess.ReadAsync(handle, buffer, offset, token);
            if (!mutated && read != 0)
            {
                mutated = true;
                await File.WriteAllBytesAsync(path, new byte[32 * 1024], token);
            }
            return read;
        });

        var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
            await access.ObserveAsync(
                path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken));

        Assert.True(mutated);
        Assert.Equal(SystemdSourceFileFailure.Inconsistent, exception.Failure);
    }

    [Theory(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task StopsAtTheFirstByteBeyondThePassedContentBudget(
        int fileBytes,
        bool expectedSuccess)
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("bounded.env");
        await File.WriteAllBytesAsync(path, new byte[fileBytes], TestContext.Current.CancellationToken);
        var bytesObserved = 0;
        var largestRequest = 0;
        var access = new LinuxSystemdSourceFileAccess(async (handle, buffer, offset, token) =>
        {
            largestRequest = Math.Max(largestRequest, buffer.Length);
            var read = await RandomAccess.ReadAsync(handle, buffer, offset, token);
            bytesObserved += read;
            return read;
        });

        if (expectedSuccess)
        {
            using var observation = await access.ObserveAsync(
                path,
                readContent: true,
                allowMissing: false,
                TestContext.Current.CancellationToken,
                maximumContentBytes: 4);
            Assert.Equal(4, observation.ContentLength);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
                await access.ObserveAsync(
                    path,
                    readContent: true,
                    allowMissing: false,
                    TestContext.Current.CancellationToken,
                    maximumContentBytes: 4));
            Assert.Equal(SystemdSourceFileFailure.LimitExceeded, exception.Failure);
        }

        Assert.Equal(fileBytes, bytesObserved);
        Assert.True(largestRequest <= 5);
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task HonorsPrecancelledCaller()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new LinuxSystemdSourceFileAccess().ObserveAsync(
                "/not/opened", readContent: true, allowMissing: false, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task DisposesDescriptorReturnedAfterCancellation()
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("late-open.env");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        SafeFileHandle? opened = null;
        var access = new LinuxSystemdSourceFileAccess(
            static (handle, buffer, offset, token) => RandomAccess.ReadAsync(handle, buffer, offset, token),
            (requestedPath, _, _) =>
            {
                var handle = File.OpenHandle(
                    requestedPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileOptions.RandomAccess);
                opened = handle;
                cancellation.Cancel();
                return handle;
            });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await access.ObserveAsync(
                path, readContent: true, allowMissing: false, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.NotNull(opened);
        Assert.True(opened.IsClosed);
    }

    [Fact(Skip = "Requires Linux openat2 and filesystem semantics.", SkipUnless = nameof(IsLinux))]
    public async Task DiscardsReadReturnedAfterCancellation()
    {
        using var fixture = new FileFixture();
        var path = fixture.Path("late-read.env");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var readCalls = 0;
        var access = new LinuxSystemdSourceFileAccess((_, buffer, _, _) =>
        {
            readCalls++;
            buffer.Span[0] = 1;
            cancellation.Cancel();
            return ValueTask.FromResult(1);
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await access.ObserveAsync(
                path, readContent: true, allowMissing: false, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, readCalls);
    }

    [Fact(Skip = "Requires non-root Linux permission semantics.", SkipUnless = nameof(IsLinux))]
    public async Task DoesNotConvertAccessDenialIntoOptionalAbsence()
    {
        if (!OperatingSystem.IsLinux())
            return;
        if (GetEffectiveUserId() == 0)
            return; // The ordinary Ubuntu CI job exercises this; the root systemd harness cannot.
        using var fixture = new FileFixture();
        var path = fixture.Path("denied.env");
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        File.SetUnixFileMode(path, UnixFileMode.None);

        var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
            await new LinuxSystemdSourceFileAccess().ObserveAsync(
                path, readContent: true, allowMissing: true, TestContext.Current.CancellationToken));

        Assert.Equal(SystemdSourceFileFailure.Unavailable, exception.Failure);
    }

    private static async Task Unsafe(LinuxSystemdSourceFileAccess access, string path)
    {
        var exception = await Assert.ThrowsAsync<SystemdSourceFileException>(async () =>
            await access.ObserveAsync(
                path, readContent: true, allowMissing: false, TestContext.Current.CancellationToken));
        Assert.Equal(SystemdSourceFileFailure.UnsafePath, exception.Failure);
    }

    private static void CreateFifo(string path)
    {
        var library = NativeLibrary.Load("libc.so.6");
        try
        {
            var mkfifo = Marshal.GetDelegateForFunctionPointer<MkFifo>(
                NativeLibrary.GetExport(library, "mkfifo"));
            Assert.Equal(0, mkfifo(path, 0x180));
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static uint GetEffectiveUserId()
    {
        var library = NativeLibrary.Load("libc.so.6");
        try
        {
            return Marshal.GetDelegateForFunctionPointer<GetEffectiveUserIdDelegate>(
                NativeLibrary.GetExport(library, "geteuid"))();
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MkFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetEffectiveUserIdDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MqOpen(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int flags,
        uint mode,
        IntPtr attributes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MqClose(int descriptor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MqUnlink([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    private sealed class FileFixture : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "serval-source-tests-" + Guid.NewGuid().ToString("N"));

        internal FileFixture() => Directory.CreateDirectory(_root);
        internal string Path(string name) => System.IO.Path.Combine(_root, name);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
