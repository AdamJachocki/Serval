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
    var result = (AgentResult.Login)await SendAsync(request, deadline.Token);
    if (result.Code == AgentResultCode.Success && !string.IsNullOrEmpty(result.Session) &&
        result.Identity is { Uid: > 0 } identity && identity.Name == username)
    {
        var validationRequest = new AgentRequest.ValidateSession("fixture-validation", result.Session);
        var validation = (AgentResult.ValidateSession)await SendAsync(validationRequest, deadline.Token);
        if (validation.Code != AgentResultCode.Success || validation.Identity != identity)
        {
            Console.WriteLine("unexpected");
        }
        else if (args.Length == 1 && args[0] is "account-invalidation" or "restart-invalidation")
        {
            var marker = "/tmp/serval-fixture-" + args[0];
            await File.WriteAllTextAsync(marker + "-ready", "ready", deadline.Token);
            while (!File.Exists(marker + "-continue"))
            {
                await Task.Delay(100, deadline.Token);
            }

            var invalidated = (AgentResult.ValidateSession)await SendAsync(
                new AgentRequest.ValidateSession("fixture-invalidated", result.Session), deadline.Token);
            Console.WriteLine(invalidated.Code == AgentResultCode.Unauthenticated &&
                invalidated.Identity is null ? args[0] + "-success" : "unexpected");
        }
        else
        {
            Console.WriteLine("success");
        }
    }
    else if (result.Code == AgentResultCode.Denied && result.Session is null && result.Identity is null)
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

static async Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
{
    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/serval/agent.sock"), cancellationToken);
    using var stream = new NetworkStream(socket, ownsSocket: false);
    await AgentProtocol.WriteRequestAsync(stream, request, cancellationToken);
    return await AgentProtocol.ReadResultAsync(stream, request, cancellationToken);
}
