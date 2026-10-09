using System.Net.Sockets;
using System.Runtime.InteropServices;
using Serval.Application.Ipc;

namespace Serval.Agent;

/// <summary>Local-only Agent transport. Peer identity comes solely from the kernel.</summary>
public sealed partial class AgentSocketServer(
    AgentConfiguration configuration,
    Func<AgentRequest, CancellationToken, Task<AgentResult>> dispatch)
{
    private const int SolSocket = 1;
    private const int SoPeerCred = 17;
    private const int MaximumClients = 32;
    private readonly TimeSpan clientDeadline = TimeSpan.FromSeconds(10);
    private readonly string socketPath = AgentConfiguration.SocketPath;
    private readonly bool validateRuntimeDirectory = true;

    internal AgentSocketServer(
        AgentConfiguration configuration,
        Func<AgentRequest, CancellationToken, Task<AgentResult>> dispatch,
        string socketPath, TimeSpan clientDeadline)
        : this(configuration, dispatch)
    {
        if (!Path.IsPathFullyQualified(socketPath))
        {
            throw new ArgumentException("Socket path must be absolute.", nameof(socketPath));
        }

        this.socketPath = socketPath;
        if (clientDeadline <= TimeSpan.Zero || clientDeadline > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(clientDeadline));
        }

        this.clientDeadline = clientDeadline;
        validateRuntimeDirectory = false;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() ||
            (validateRuntimeDirectory && (configuration.WebUid == 0 || configuration.WebGid == 0)))
        {
            throw new InvalidOperationException("Agent socket configuration is invalid.");
        }

        if (validateRuntimeDirectory)
        {
            configuration.ValidateRuntimeDirectory();
        }

        if (File.Exists(socketPath))
        {
            throw new InvalidOperationException("Agent socket path exists.");
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        try
        {
            if (validateRuntimeDirectory && Chown(socketPath, 0, configuration.WebGid) != 0)
            {
                throw new InvalidOperationException("Agent socket ownership cannot be established.");
            }
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite);
            listener.Listen(MaximumClients);
            using var limit = new SemaphoreSlim(MaximumClients);
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket accepted;
                try
                {
                    accepted = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!limit.Wait(0, cancellationToken))
                {
                    accepted.Dispose();
                    continue;
                }

                _ = HandleAndReleaseAsync(accepted, limit, cancellationToken);
            }
        }
        finally
        {
            File.Delete(socketPath);
        }
    }

    private async Task HandleAndReleaseAsync(
        Socket client, SemaphoreSlim limit, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
        {
            // The transport is unavailable for this peer. Keep the listener alive.
        }
        finally
        {
            client.Dispose();
            limit.Release();
        }
    }

    private async Task HandleAsync(Socket client, CancellationToken cancellationToken)
    {
        if (!IsAuthorizedPeer(client))
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(clientDeadline);
        using var stream = new NetworkStream(client, ownsSocket: false);
        AgentRequest request;
        try
        {
            request = await AgentProtocol.ReadRequestAsync(stream, deadline.Token).ConfigureAwait(false);
        }
        catch (AgentProtocolException)
        {
            await TryWriteInvalidRequestAsync(stream, deadline.Token).ConfigureAwait(false);
            return;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        AgentResult result;
        try
        {
            result = await dispatch(request, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = FailureFor(request);
        }

        try
        {
            await AgentProtocol.WriteResultAsync(stream, result, deadline.Token).ConfigureAwait(false);
        }
        catch (AgentProtocolException)
        {
            await TryWriteUnavailableAsync(stream, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The peer may have disconnected. No privileged result has been published.
        }
    }

    private static async Task TryWriteUnavailableAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await AgentProtocol.WriteFailureAsync(stream, AgentResultCode.Unavailable,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The peer may have disconnected.
        }
    }

    private static async Task TryWriteInvalidRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await AgentProtocol.WriteInvalidRequestAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The client may have left after sending a malformed request.
        }
    }

    private bool IsAuthorizedPeer(Socket client)
    {
        var credentials = new UCred();
        var length = (uint)Marshal.SizeOf<UCred>();
        var descriptor = client.SafeHandle.DangerousGetHandle().ToInt32();
        return Getsockopt(descriptor, SolSocket, SoPeerCred, ref credentials, ref length) == 0 &&
            length == Marshal.SizeOf<UCred>() && credentials.Uid == configuration.WebUid;
    }

    private static AgentResult FailureFor(AgentRequest request) => request switch
    {
        AgentRequest.Login => new AgentResult.Login(AgentResultCode.Unavailable, null),
        AgentRequest.Logout => new AgentResult.Logout(AgentResultCode.Unavailable),
        AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Unavailable, null),
        AgentRequest.InspectService => new AgentResult.InspectService(AgentResultCode.Unavailable, null),
        AgentRequest.ListGrants => new AgentResult.ListGrants(AgentResultCode.Unavailable, null),
        AgentRequest.AddGrant => new AgentResult.AddGrant(AgentResultCode.Unavailable, null),
        AgentRequest.RemoveGrant => new AgentResult.RemoveGrant(AgentResultCode.Unavailable),
        _ => throw new AgentProtocolException(),
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct UCred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [LibraryImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static partial int Getsockopt(
        int socket, int level, int option, ref UCred credentials, ref uint length);

    [LibraryImport("libc", EntryPoint = "chown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Chown(string path, uint owner, uint group);
}
