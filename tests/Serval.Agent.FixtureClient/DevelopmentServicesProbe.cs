using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Serval.Application.Ipc;

internal static partial class DevelopmentServicesProbe
{
    private static int failedAssertion;
    private static string phase = "input";
    private static readonly (string Name, string State)[] Samples =
    [
        ("serval-demo-running.service", "active"),
        ("serval-demo-inactive.service", "inactive"),
        ("serval-demo-failed.service", "failed"),
    ];

    public static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var admin = await Console.In.ReadLineAsync(deadline.Token) ?? throw new InvalidOperationException();
            var password = await Console.In.ReadLineAsync(deadline.Token) ?? throw new InvalidOperationException();
            var deniedUser = await Console.In.ReadLineAsync(deadline.Token) ?? throw new InvalidOperationException();
            var deniedPassword = await Console.In.ReadLineAsync(deadline.Token) ?? throw new InvalidOperationException();
            await CheckAsync(admin, password, true, deadline.Token);
            await CheckAsync(deniedUser, deniedPassword, false, deadline.Token);
            var anonymous = (AgentResult.InspectService)await SendAsync(
                new AgentRequest.InspectService("anonymous", new string('a', 64), Samples[0].Name), deadline.Token);
            Require(anonymous.Code == AgentResultCode.Unauthenticated && anonymous.Service is null);
            Console.WriteLine("Development Agent and HTTP authorization checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            // Credentials, cookies and protocol payloads must never reach diagnostics.
            Console.Error.WriteLine($"Development authorization probe failed (assertion line {failedAssertion}, phase {phase}, type {exception.GetType().Name}).");
            return 1;
        }
    }

    private static async Task CheckAsync(string username, string password, bool allowed, CancellationToken token)
    {
        var login = (AgentResult.Login)await SendAsync(new AgentRequest.Login("login", username, password), token);
        Require(login.Code == AgentResultCode.Success && login.Session is not null);
        var session = login.Session!;
        var list = (AgentResult.ListServices)await SendAsync(new AgentRequest.ListServices("list", session), token);
        Require(list.Code == AgentResultCode.Success && list.Services is not null);
        foreach (var (name, state) in Samples)
        {
            Require(list.Services!.Any(service => service.Id == name && service.ActiveState == state) == allowed);
            var detail = (AgentResult.InspectService)await SendAsync(new AgentRequest.InspectService("detail", session, name), token);
            Require(allowed
                ? detail.Code == AgentResultCode.Success && detail.Service?.ActiveState == state
                : detail.Code == AgentResultCode.Denied && detail.Service is null);
        }

        string[] forbidden = ["../etc/passwd", "name;id.service", "serval-agent.service", "serval-development-protected-alias.service"];
        foreach (var name in forbidden)
        {
            var detail = (AgentResult.InspectService)await SendAsync(new AgentRequest.InspectService("forbidden", session, name), token);
            Require(detail.Code is AgentResultCode.Denied or AgentResultCode.NotFound or AgentResultCode.InvalidRequest && detail.Service is null);
            Require(!list.Services!.Any(service => service.Id == name));
        }

        phase = "HttpLogin";
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(5) };
        using var anonymous = await http.GetAsync("/Services", token);
        Require(anonymous.StatusCode == HttpStatusCode.Redirect);
        var html = await http.GetStringAsync("/Login", token);
        var match = AntiforgeryToken().Match(html);
        Require(match.Success);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        });
        using var response = await http.PostAsync("/Login", form, token);
        Require(response.StatusCode == HttpStatusCode.Redirect);
        // Browsers treat localhost as secure. HttpClient does not, so forward the
        // returned Secure session cookie explicitly for this loopback HTTP probe.
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-ServalSession=", StringComparison.Ordinal)).Split(';')[0];
        http.DefaultRequestHeaders.Add("Cookie", cookie);
        phase = "HttpServices";
        var listing = await http.GetStringAsync("/Services", token);
        foreach (var (name, state) in Samples)
        {
            Require(listing.Contains(name, StringComparison.Ordinal) == allowed);
            using var detail = await http.GetAsync("/Services/Details?service=" + Uri.EscapeDataString(name), token);
            Require(detail.StatusCode == (allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound));
            if (allowed)
            {
                var body = await detail.Content.ReadAsStringAsync(token);
                Require(body.Contains(name, StringComparison.Ordinal) && body.Contains(">" + state + "</dd>", StringComparison.Ordinal));
            }
        }
        foreach (var name in forbidden)
        {
            using var detail = await http.GetAsync("/Services/Details?service=" + Uri.EscapeDataString(name), token);
            Require(detail.StatusCode == HttpStatusCode.NotFound);
        }
        await SendAsync(new AgentRequest.Logout("logout", session), token);
    }

    private static async Task<AgentResult> SendAsync(AgentRequest request, CancellationToken token)
    {
        phase = request.GetType().Name;
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/serval/agent.sock"), token);
        using var stream = new NetworkStream(socket);
        await AgentProtocol.WriteRequestAsync(stream, request, token);
        return await AgentProtocol.ReadResultAsync(stream, request, token);
    }

    private static void Require(bool condition, [CallerLineNumber] int line = 0)
    {
        if (!condition) { failedAssertion = line; throw new InvalidOperationException(); }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
