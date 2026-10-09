using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Serval.Agent;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Agent.Tests;

public sealed partial class AgentSocketServerTests
{
    [Fact]
    public async Task KernelPeerUidAndStrictFrameGateDispatch()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var uid = GetUid();
        var directory = Path.Combine(Path.GetTempPath(), $"serval-socket-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var socketPath = Path.Combine(directory, "agent.sock");
        var dispatched = 0;
        try
        {
            await RunServerAsync(uid + 1, socketPath, async () =>
            {
                using var denied = await ConnectAsync(socketPath);
                var deniedStream = new NetworkStream(denied, ownsSocket: false);
                Assert.Equal(0, await deniedStream.ReadAsync(new byte[1], TestContext.Current.CancellationToken));
                Assert.Equal(0, Volatile.Read(ref dispatched));
            }, _ =>
            {
                Interlocked.Increment(ref dispatched);
                return Task.FromResult<AgentResult>(new AgentResult.Logout(AgentResultCode.Success));
            });

            await RunServerAsync(uid, socketPath, async () =>
            {
                using (var malformed = await ConnectAsync(socketPath))
                {
                    var stream = new NetworkStream(malformed, ownsSocket: false);
                    var body = Encoding.UTF8.GetBytes("""{"version":1,"operation":"Logout","correlationId":"bad","session":"x","uid":0}""");
                    var header = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
                    await stream.WriteAsync(header, TestContext.Current.CancellationToken);
                    await stream.WriteAsync(body, TestContext.Current.CancellationToken);
                    Assert.Contains("InvalidRequest", await ReadFrameAsync(stream));
                    Assert.Equal(0, Volatile.Read(ref dispatched));
                }

                using var allowed = await ConnectAsync(socketPath);
                var allowedStream = new NetworkStream(allowed, ownsSocket: false);
                await AgentProtocol.WriteRequestAsync(allowedStream,
                    new AgentRequest.Logout("allowed", "placeholder"),
                    TestContext.Current.CancellationToken);
                Assert.Contains("Success", await ReadFrameAsync(allowedStream));
                Assert.Equal(1, Volatile.Read(ref dispatched));

                using var stalled = await ConnectAsync(socketPath);
                await Task.Delay(400, TestContext.Current.CancellationToken);
                Assert.Equal(0, await stalled.ReceiveAsync(new byte[1], SocketFlags.None,
                    TestContext.Current.CancellationToken));
                Assert.Equal(1, Volatile.Read(ref dispatched));
            }, _ =>
            {
                Interlocked.Increment(ref dispatched);
                return Task.FromResult<AgentResult>(new AgentResult.Logout(AgentResultCode.Success));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: false);
        }
    }

    private static async Task RunServerAsync(uint expectedUid, string socketPath,
        Func<Task> verify, Func<AgentRequest, Task<AgentResult>> dispatch)
    {
        var configuration = new AgentConfiguration(expectedUid, 1, "sudo", 1, TimeSpan.FromMinutes(15));
        var server = new AgentSocketServer(configuration, (request, _) => dispatch(request),
            socketPath, TimeSpan.FromMilliseconds(200));
        using var stop = new CancellationTokenSource();
        var run = server.RunAsync(stop.Token);
        try
        {
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(socketPath))
            {
                await Task.Delay(10, ready.Token);
            }

            await verify();
        }
        finally
        {
            stop.Cancel();
            await run;
        }
    }

    private static async Task<Socket> ConnectAsync(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path),
                TestContext.Current.CancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadFrameAsync(Stream stream)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        Assert.InRange(length, 1, 1024);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(body);
    }

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();
}
