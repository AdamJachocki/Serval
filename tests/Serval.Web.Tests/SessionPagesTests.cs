using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;
using Serval.Web;
using Serval.Web.Pages;
using Xunit;

namespace Serval.Web.Tests;

public sealed class SessionPagesTests
{
    [Fact]
    public async Task LoginIssuesOnlySecureSessionCookie()
    {
        var agent = new FakeClient(_ => new AgentResult.Login(AgentResultCode.Success, "opaque-token"));
        var context = new DefaultHttpContext();
        var page = Attach(new LoginModel(agent)
        {
            Username = "alice",
            Password = "synthetic-password",
        }, context);

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(TestContext.Current.CancellationToken));
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("__Host-ServalSession=opaque-token", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, page.Password);
    }

    [Fact]
    public async Task LogoutRevokesAtAgentAndClearsCookie()
    {
        var agent = new FakeClient(request => request is AgentRequest.Logout
            ? new AgentResult.Logout(AgentResultCode.Success)
            : throw new InvalidOperationException());
        var context = ContextWithCookie();
        var page = Attach(new LogoutModel(agent), context);

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(TestContext.Current.CancellationToken));
        Assert.IsType<AgentRequest.Logout>(Assert.Single(agent.Requests));
        Assert.Contains("__Host-ServalSession=", context.Response.Headers.SetCookie.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClientIpChangeDoesNotChangeDelegatedSession()
    {
        var context = ContextWithCookie();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.Equal("opaque-token", ServalSessionCookie.Read(context.Request));
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.2");
        Assert.Equal("opaque-token", ServalSessionCookie.Read(context.Request));
    }

    [Fact]
    public async Task FailedLoginDoesNotIssueCookieOrExposeReason()
    {
        var agent = new FakeClient(_ => new AgentResult.Login(AgentResultCode.Denied, null));
        var context = new DefaultHttpContext();
        var page = Attach(new LoginModel(agent)
        {
            Username = "alice",
            Password = "wrong",
        }, context);

        Assert.IsType<PageResult>(await page.OnPostAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
        Assert.NotNull(page.Error);
        Assert.DoesNotContain("wrong", page.Error, StringComparison.Ordinal);
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
