using System.Net.Sockets;
using Serval.Application.Ipc;

// The runner supplies username and password on stdin. Only a fixed result category is printed.
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
try
{
    var username = await Console.In.ReadLineAsync(deadline.Token);
    var password = await Console.In.ReadLineAsync(deadline.Token);
    if (username is null || password is null)
    {
        return 2;
    }

    var request = new AgentRequest.Login("fixture-login", username, password);
    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/serval/agent.sock"), deadline.Token);
    using var stream = new NetworkStream(socket, ownsSocket: false);
    await AgentProtocol.WriteRequestAsync(stream, request, deadline.Token);
    var result = (AgentResult.Login)await AgentProtocol.ReadResultAsync(stream, request, deadline.Token);
    if (result.Code == AgentResultCode.Success && !string.IsNullOrEmpty(result.Session))
    {
        Console.WriteLine("success");
    }
    else if (result.Code == AgentResultCode.Denied && result.Session is null)
    {
        Console.WriteLine("denied");
    }
    else
    {
        Console.WriteLine("unexpected");
    }
}
catch (Exception exception) when (exception is SocketException or IOException or
    EndOfStreamException or OperationCanceledException or AgentProtocolException)
{
    Console.WriteLine("unavailable");
}

return 0;
