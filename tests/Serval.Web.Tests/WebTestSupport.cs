using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serval.Application.Ipc;
using Serval.Application.Services;
using Xunit;

namespace Serval.Web.Tests;

internal sealed class TestAgentClient : IAgentIpcClient
{
    private readonly Func<AgentRequest, CancellationToken, Task<AgentResult>>? handler;

    public TestAgentClient(
        IReadOnlyList<AgentService>? services = null,
        IReadOnlyList<AgentGrant>? grants = null,
        AgentResult.Login? loginResult = null,
        Func<AgentRequest, CancellationToken, Task<AgentResult>>? handler = null,
        IEnumerable<string>? favorites = null)
    {
        foreach (var favorite in favorites ?? [])
        {
            FavoriteIds.Add(favorite);
        }

        Services = services ??
        [
            new("nginx.service", "Serwer WWW", "loaded", "active", "running"),
            new("backup.service", "Kopia zapasowa", "loaded", "failed", "failed"),
            new("worker.service", "Zadania w tle", "loaded", "inactive", "dead"),
        ];
        Grants = grants ?? [new AgentGrant(1, "User", "alice", "Service.View", "nginx.service")];
        LoginResult = loginResult ?? new AgentResult.Login(AgentResultCode.Denied, null);
        this.handler = handler;
    }

    public ConcurrentQueue<AgentRequest> Requests { get; } = new();

    public IReadOnlyList<AgentService> Services { get; }

    public HashSet<string> FavoriteIds { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<AgentGrant> Grants { get; }

    public AgentResult.Login LoginResult { get; }

    public async Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        if (handler is not null)
        {
            return await handler(request, cancellationToken);
        }

        return request switch
        {
            AgentRequest.Login => LoginResult,
            AgentRequest.Logout => new AgentResult.Logout(AgentResultCode.Success),
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Success, Services,
                Services.Where(service => FavoriteIds.Contains(service.Id)).Select(service => service.Id).ToArray()),
            AgentRequest.AddFavorite add => AddFavorite(add),
            AgentRequest.RemoveFavorite remove => RemoveFavorite(remove),
            AgentRequest.InspectService inspection => new AgentResult.InspectService(
                Services.FirstOrDefault(service => string.Equals(service.Id, inspection.Service, StringComparison.Ordinal)) is { } service
                    ? AgentResultCode.Success
                    : AgentResultCode.NotFound,
                Services.FirstOrDefault(service => string.Equals(service.Id, inspection.Service, StringComparison.Ordinal))),
            AgentRequest.ListGrants => new AgentResult.ListGrants(AgentResultCode.Success, Grants),
            AgentRequest.AddGrant => new AgentResult.AddGrant(AgentResultCode.Success, 2),
            AgentRequest.RemoveGrant => new AgentResult.RemoveGrant(AgentResultCode.Success),
            AgentRequest.ListEnvironment environment => new AgentResult.ListEnvironment(
                AgentResultCode.Success,
                new AgentEnvironment(environment.Service, EnvironmentModelScope.SupportedDeclarations,
                    [], [], false)),
            AgentRequest.RevealEnvironmentVariable => new AgentResult.RevealEnvironmentVariable(
                AgentResultCode.Denied, null),
            _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
        };
    }

    private AgentResult.AddFavorite AddFavorite(AgentRequest.AddFavorite request)
    {
        if (!Services.Any(service => string.Equals(service.Id, request.Service, StringComparison.Ordinal)))
        {
            return new AgentResult.AddFavorite(AgentResultCode.NotFound);
        }

        FavoriteIds.Add(request.Service);
        return new AgentResult.AddFavorite(AgentResultCode.Success);
    }

    private AgentResult.RemoveFavorite RemoveFavorite(AgentRequest.RemoveFavorite request)
    {
        FavoriteIds.Remove(request.Service);
        return new AgentResult.RemoveFavorite(AgentResultCode.Success);
    }
}

internal sealed class ServalWebFactory(TestAgentClient agent) : WebApplicationFactory<Program>
{
    public TestAgentClient Agent { get; } = agent;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStaticWebAssets();
        builder.ConfigureTestServices(services => services.AddSingleton<IAgentIpcClient>(Agent));
    }
}

internal static partial class WebTestSupport
{
    public const string SessionCookie = "__Host-ServalSession=opaque-token";

    public static HttpClient CreateClient(ServalWebFactory factory, bool allowRedirect = false) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowRedirect,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    public static HttpRequestMessage AuthenticatedGet(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", SessionCookie);
        return request;
    }

    public static string AntiForgeryToken(string html)
    {
        var match = AntiForgeryInput().Match(html);
        Assert.True(match.Success, "Expected an anti-forgery token in rendered markup.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("<input[^>]+name=\"__RequestVerificationToken\"[^>]+value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryInput();
}
