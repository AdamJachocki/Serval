using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;
using Serval.Web;
using Xunit;

namespace Serval.Web.Tests;

public sealed class ServicePagesTests
{
    [Theory]
    [InlineData(AgentResultCode.Denied, 404)]
    [InlineData(AgentResultCode.NotFound, 404)]
    [InlineData(AgentResultCode.InvalidRequest, 404)]
    [InlineData(AgentResultCode.Unauthenticated, 401)]
    public async Task RevealFailuresAreIndistinguishableAndSessionExpiryClearsCookie(AgentResultCode code, int status)
    {
        var agent = new FakeClient(_ => new AgentResult.RevealEnvironmentVariable(code, null));
        var context = ContextWithCookie();
        var page = Attach(new Serval.Web.Pages.Services.DetailsModel(agent), context);
        Assert.Equal(status, Assert.IsAssignableFrom<StatusCodeResult>(await page.OnPostRevealAsync(
            "worker.service", "CONFIG", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        if (code == AgentResultCode.Unauthenticated)
            Assert.Contains("__Host-ServalSession=", context.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UnsupportedConfiguration", "Ta konfiguracja nie jest obsługiwana.")]
    [InlineData("InconsistentSnapshot", "Konfiguracja zmieniła się podczas odczytu. Spróbuj ponownie.")]
    [InlineData("arbitrary-private-error", "Środowisko jest chwilowo niedostępne.")]
    public async Task EnvironmentErrorsUseOnlyFixedMessages(string failure, string expected)
    {
        var agent = new FakeClient(_ => new AgentResult.ListEnvironment(AgentResultCode.Unavailable, null, failure));
        var page = Attach(new Serval.Web.Pages.Services.DetailsModel(agent), ContextWithCookie());
        var result = Assert.IsType<JsonResult>(await page.OnGetEnvironmentAsync("worker.service", TestContext.Current.CancellationToken));
        Assert.Equal(503, result.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.Equal(expected, json.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(AgentResultCode.Denied, 404)]
    [InlineData(AgentResultCode.NotFound, 404)]
    [InlineData(AgentResultCode.Unauthenticated, 401)]
    public async Task EnvironmentFailuresDoNotExposeMetadata(AgentResultCode code, int status)
    {
        var agent = new FakeClient(_ => new AgentResult.ListEnvironment(code, null));
        var context = ContextWithCookie();
        var page = Attach(new Serval.Web.Pages.Services.DetailsModel(agent), context);
        Assert.Equal(status, Assert.IsAssignableFrom<StatusCodeResult>(await page.OnGetEnvironmentAsync(
            "worker.service", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        if (code == AgentResultCode.Unauthenticated)
            Assert.Contains("__Host-ServalSession=", context.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Environment.View")]
    [InlineData("Environment.Reveal")]
    public async Task GrantFormSendsOnlySelectedPermission(string operation)
    {
        var agent = new FakeClient(_ => new AgentResult.AddGrant(AgentResultCode.Success, 1));
        var page = Attach(new Serval.Web.Pages.Grants.IndexModel(agent), ContextWithCookie());
        page.Subject = "alice";
        page.Service = "worker.service";
        page.Operation = operation;
        Assert.IsType<RedirectToPageResult>(await page.OnPostAddAsync(TestContext.Current.CancellationToken));
        Assert.Equal(operation, Assert.IsType<AgentRequest.AddGrant>(Assert.Single(agent.Requests)).Operation);
    }

    [Fact]
    public async Task AnonymousRequestNeverCallsAgentOrSeesServiceMetadata()
    {
        var agent = new FakeClient(_ => throw new InvalidOperationException("Unexpected Agent request"));
        var page = Attach(new Serval.Web.Pages.Services.IndexModel(agent), new DefaultHttpContext());

        Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));
        Assert.Empty(agent.Requests);
        Assert.Empty(page.Services);
    }

    [Fact]
    public async Task InventoryShowsOnlyAgentFilteredCanonicalRecords()
    {
        var agent = new FakeClient(_ => new AgentResult.ListServices(AgentResultCode.Success,
            [new AgentService("visible.service", "Visible", "loaded", "active", "running")]));
        var page = Attach(new Serval.Web.Pages.Services.IndexModel(agent), ContextWithCookie());

        Assert.IsType<PageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));
        Assert.Equal("visible.service", Assert.Single(page.Services).Id);
        Assert.IsType<AgentRequest.ListServices>(Assert.Single(agent.Requests));
    }

    [Fact]
    public async Task StaleAgentSessionRequiresLoginAndClearsCookie()
    {
        var agent = new FakeClient(_ => new AgentResult.ListServices(AgentResultCode.Unauthenticated, null));
        var context = ContextWithCookie();
        var page = Attach(new Serval.Web.Pages.Services.IndexModel(agent), context);

        Assert.IsType<RedirectToPageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));
        Assert.Empty(page.Services);
        Assert.Contains("__Host-ServalSession=", context.Response.Headers.SetCookie.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectionHidesDeniedAndProtectedTargets()
    {
        var agent = new FakeClient(_ => new AgentResult.InspectService(AgentResultCode.NotFound, null));
        var page = Attach(new Serval.Web.Pages.Services.DetailsModel(agent), ContextWithCookie());

        Assert.IsType<NotFoundResult>(await page.OnGetAsync("hidden.service",
            TestContext.Current.CancellationToken));
        Assert.Null(page.Service);
    }

    [Fact]
    public async Task GrantPageRequiresAgentAdministratorDecision()
    {
        var agent = new FakeClient(request => request switch
        {
            AgentRequest.ListGrants => new AgentResult.ListGrants(AgentResultCode.Denied, null),
            AgentRequest.AddGrant => new AgentResult.AddGrant(AgentResultCode.Denied, null),
            _ => throw new InvalidOperationException(),
        });
        var page = Attach(new Serval.Web.Pages.Grants.IndexModel(agent), ContextWithCookie());

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnGetAsync(
            TestContext.Current.CancellationToken)).StatusCode);
        page.SubjectKind = "User";
        page.Subject = "alice";
        page.Service = "worker.service";
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await page.OnPostAddAsync(
            TestContext.Current.CancellationToken)).StatusCode);
        Assert.Contains(agent.Requests, request => request is AgentRequest.AddGrant);
        Assert.Empty(page.Grants);
    }

    private static T Attach<T>(T page, HttpContext context) where T : PageModel
    {
        page.PageContext = new PageContext { HttpContext = context };
        return page;
    }

    private static DefaultHttpContext ContextWithCookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "__Host-ServalSession=opaque-token";
        return context;
    }

    private sealed class FakeClient(Func<AgentRequest, AgentResult> respond) : IAgentIpcClient
    {
        public List<AgentRequest> Requests { get; } = [];

        public Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
