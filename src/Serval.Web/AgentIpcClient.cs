using System.Net.Sockets;
using Serval.Application.Ipc;

namespace Serval.Web;

public interface IAgentIpcClient
{
    Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken);
}

/// <summary>One bounded request per local Agent connection.</summary>
public sealed class AgentIpcClient : IAgentIpcClient
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(12);
    private const string SocketPath = "/run/serval/agent.sock";

    public async Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), deadline.Token)
                .ConfigureAwait(false);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await AgentProtocol.WriteRequestAsync(stream, request, deadline.Token).ConfigureAwait(false);
            return await AgentProtocol.ReadResultAsync(stream, request, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is SocketException or IOException or
            AgentProtocolException or OperationCanceledException)
        {
            return Failure(request);
        }
    }

    private static AgentResult Failure(AgentRequest request) => request switch
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
}
