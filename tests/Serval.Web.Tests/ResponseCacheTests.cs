using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serval.Application.Ipc;
using Serval.Web;
using Xunit;

namespace Serval.Web.Tests;

public sealed class ResponseCacheTests
{
    [Theory]
    [InlineData("/Services")]
    [InlineData("/Services/Details?service=visible.service")]
    [InlineData("/Grants")]
    public async Task SessionDependentPagesDisableHttpCaching(string path)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IAgentIpcClient, PermittingAgentClient>();
                });
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", "__Host-ServalSession=opaque-token");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    private sealed class PermittingAgentClient : IAgentIpcClient
    {
        public Task<AgentResult> SendAsync(
            AgentRequest request,
            CancellationToken cancellationToken) => Task.FromResult<AgentResult>(request switch
            {
                AgentRequest.ListServices => new AgentResult.ListServices(
                    AgentResultCode.Success,
                    [new AgentService("visible.service", "Visible", "loaded", "active", "running")]),
                AgentRequest.InspectService => new AgentResult.InspectService(
                    AgentResultCode.Success,
                    new AgentService("visible.service", "Visible", "loaded", "active", "running")),
                AgentRequest.ListGrants => new AgentResult.ListGrants(
                    AgentResultCode.Success,
                    [new AgentGrant(1, "User", "alice", "Service.View", "visible.service")]),
                _ => throw new InvalidOperationException("Unexpected Agent request."),
            });
    }
}
