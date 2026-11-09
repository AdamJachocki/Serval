using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Primitives;
using Serval.Application.Ipc;
using Serval.Web.Pages.Services;
using Xunit;

namespace Serval.Web.Tests;

public sealed class FavoriteServicesTests
{
    private static readonly AgentService[] Inventory =
    [
        new("alpha.service", "Alpha", "loaded", "active", "running"),
        new("beta.service", "Beta", "loaded", "failed", "failed"),
        new("gamma.service", "Gamma", "loaded", "inactive", "dead"),
        new("omega.service", "Omega", "loaded", "active", "running"),
    ];

    [Fact]
    public async Task InventorySplitsFavoritesFromOthersKeepingAgentOrder()
    {
        var page = Attach(new IndexModel(ListClient(["gamma.service", "alpha.service"])), Authenticated());

        Assert.IsType<PageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));

        Assert.True(page.HasFavorites);
        Assert.Equal(["alpha.service", "gamma.service"], page.Favorites.Select(service => service.Id));
        Assert.Equal(["beta.service", "omega.service"], page.Others.Select(service => service.Id));
        Assert.Equal(4, page.Services.Count);
        Assert.True(page.IsFavorite(Inventory[0]));
        Assert.False(page.IsFavorite(Inventory[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoryWithoutFavoritesIsASingleList(bool emptyList)
    {
        var page = Attach(new IndexModel(ListClient(emptyList ? [] : null)), Authenticated());

        Assert.IsType<PageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));

        Assert.False(page.HasFavorites);
        Assert.Empty(page.Favorites);
        Assert.Equal(4, page.Others.Count);
    }

    [Fact]
    public async Task FavoriteOutsideTheReturnedListMakesTheInventoryUnavailable()
    {
        var page = Attach(new IndexModel(ListClient(["alpha.service", "ghost.service"])), Authenticated());

        Assert.IsType<PageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(page.Error);
        Assert.Empty(page.Services);
        Assert.Empty(page.Favorites);
        Assert.Empty(page.Others);
    }

    [Theory]
    [InlineData(AgentResultCode.Unavailable)]
    [InlineData(AgentResultCode.Denied)]
    public async Task InventoryFailureShowsNoSections(AgentResultCode code)
    {
        var agent = new FakeClient(_ => new AgentResult.ListServices(code, null, ["alpha.service"]));
        var page = Attach(new IndexModel(agent), Authenticated());

        Assert.IsType<PageResult>(await page.OnGetAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(page.Error);
        Assert.False(page.HasFavorites);
        Assert.Empty(page.Services);
        Assert.Empty(page.Others);
    }

    [Theory]
    [InlineData("limit", IndexModel.FavoriteLimitMessage)]
    [InlineData("failed", IndexModel.GenericFavoriteFailure)]
    [InlineData("unknown", null)]
    [InlineData("<script>", null)]
    [InlineData(null, null)]
    public async Task StatusQueryValueMapsToFixedMessagesAndUnknownValuesAreIgnored(string? status, string? expected)
    {
        var page = Attach(new IndexModel(ListClient([])), Authenticated());
        page.FavoriteStatus = status;

        await page.OnGetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, page.FavoriteMessage);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task ToggleCallsAgentAndRedirectsToTheListWithFiltersOnly(string action)
    {
        var agent = new FakeClient(request => request is AgentRequest.AddFavorite
            ? new AgentResult.AddFavorite(AgentResultCode.Success)
            : new AgentResult.RemoveFavorite(AgentResultCode.Success));
        var page = Attach(new IndexModel(agent), Authenticated(Form(("q", "alp"), ("state", "active"))));

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            "alpha.service", action, TestContext.Current.CancellationToken));

        Assert.Equal("/Services/Index", redirect.PageName);
        Assert.Equal("alp", redirect.RouteValues!["q"]);
        Assert.Equal("active", redirect.RouteValues["state"]);
        Assert.Null(redirect.RouteValues["favorite"]);
        var request = Assert.Single(agent.Requests);
        if (action == "add")
        {
            Assert.Equal("alpha.service", Assert.IsType<AgentRequest.AddFavorite>(request).Service);
        }
        else
        {
            Assert.Equal("alpha.service", Assert.IsType<AgentRequest.RemoveFavorite>(request).Service);
        }

        Assert.Equal("opaque-token", action == "add"
            ? ((AgentRequest.AddFavorite)request).Session : ((AgentRequest.RemoveFavorite)request).Session);
    }

    [Fact]
    public async Task ToggleWithoutSessionNeverCallsTheAgent()
    {
        var agent = new FakeClient(_ => throw new InvalidOperationException("Unexpected Agent request"));
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var page = Attach(new IndexModel(agent), context);

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            "alpha.service", "add", TestContext.Current.CancellationToken));

        Assert.Equal("/Login", redirect.PageName);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task StaleSessionClearsTheCookieAndRedirectsToLogin()
    {
        var agent = new FakeClient(_ => new AgentResult.AddFavorite(AgentResultCode.Unauthenticated));
        var context = Authenticated(Form(("service", "alpha.service")));
        var page = Attach(new IndexModel(agent), context);

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            "alpha.service", "add", TestContext.Current.CancellationToken));

        Assert.Equal("/Login", redirect.PageName);
        Assert.Contains("__Host-ServalSession=", context.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentResultCode.NotFound, "failed")]
    [InlineData(AgentResultCode.InvalidRequest, "failed")]
    [InlineData(AgentResultCode.Unavailable, "failed")]
    [InlineData(AgentResultCode.Denied, "failed")]
    [InlineData(AgentResultCode.LimitReached, "limit")]
    public async Task RejectedAddUsesOnlyFixedStatusCodes(AgentResultCode code, string expected)
    {
        var agent = new FakeClient(_ => new AgentResult.AddFavorite(code));
        var page = Attach(new IndexModel(agent), Authenticated(Form(("service", "alpha.service"))));

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            "alpha.service", "add", TestContext.Current.CancellationToken));

        Assert.Equal("/Services/Index", redirect.PageName);
        Assert.Equal(expected, redirect.RouteValues!["favorite"]);
    }

    [Fact]
    public async Task InjectedReturnTargetsAndUnboundedFiltersAreNeverReflected()
    {
        var agent = new FakeClient(_ => new AgentResult.AddFavorite(AgentResultCode.Success));
        var form = Form(
            ("returnUrl", "https://evil.example/"), ("redirect", "//evil.example"),
            ("q", new string('x', IndexModel.MaximumSearchLength + 1)),
            ("state", "active"), ("state", "failed"));
        var page = Attach(new IndexModel(agent), Authenticated(form));

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            "alpha.service", "add", TestContext.Current.CancellationToken));

        Assert.Equal("/Services/Index", redirect.PageName);
        Assert.Null(redirect.RouteValues!["q"]);
        Assert.Null(redirect.RouteValues["state"]);
        Assert.DoesNotContain(redirect.RouteValues, pair => pair.Key is "returnUrl" or "redirect");
        Assert.Null(redirect.Host);
        Assert.Null(redirect.Protocol);
    }

    [Theory]
    [InlineData("alpha.service", "toggle")]
    [InlineData("alpha.service", null)]
    [InlineData(null, "add")]
    [InlineData("", "add")]
    public async Task MalformedToggleIsRejectedWithoutCallingTheAgent(string? service, string? action)
    {
        var agent = new FakeClient(_ => throw new InvalidOperationException("Unexpected Agent request"));
        var page = Attach(new IndexModel(agent), Authenticated(Form(("service", service ?? string.Empty))));

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            service, action, TestContext.Current.CancellationToken));

        Assert.Equal("failed", redirect.RouteValues!["favorite"]);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task OverLengthServiceNameIsRejectedWithoutCallingTheAgent()
    {
        var agent = new FakeClient(_ => throw new InvalidOperationException("Unexpected Agent request"));
        var name = new string('a', IndexModel.MaximumServiceLength + 1);
        var page = Attach(new IndexModel(agent), Authenticated(Form(("service", name))));

        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostFavoriteAsync(
            name, "add", TestContext.Current.CancellationToken));

        Assert.Equal("failed", redirect.RouteValues!["favorite"]);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task RenderedInventoryShowsBothSectionsWithEncodedAccessibleNames()
    {
        var services = new AgentService[]
        {
            new("a<b>.service", "Specjalna", "loaded", "active", "running"),
            new("plain.service", "Zwykła", "loaded", "inactive", "dead"),
        };
        await using var factory = new ServalWebFactory(new TestAgentClient(services, favorites: ["a<b>.service"]));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">Ulubione</h2>", html, StringComparison.Ordinal);
        Assert.Contains(">Pozostałe usługi</h2>", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf(">Ulubione</h2>", StringComparison.Ordinal) <
            html.IndexOf(">Pozostałe usługi</h2>", StringComparison.Ordinal));
        Assert.Contains("a&lt;b&gt;.service", html, StringComparison.Ordinal);
        Assert.Contains("Usuń a<b>.service z ulubionych", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("Dodaj plain.service do ulubionych", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("a<b>.service", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(html, "<table class=\"service-table\""));
        Assert.Equal(2, CountOf(html, "aria-pressed=\""));
        Assert.Contains("aria-pressed=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", html, StringComparison.Ordinal);
        Assert.Equal(10, CountOf(html, "<th scope=\"col\">"));
        Assert.Contains("Wyświetlono 2 z 2 usług", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderedInventoryWithoutFavoritesHasNoHeadingsAndOneTable()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Ulubione</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pozostałe usługi", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(html, "<table class=\"service-table\""));
        Assert.Equal(5, CountOf(html, "<th scope=\"col\">"));
        Assert.Equal(3, CountOf(html, "aria-pressed=\"false\""));
    }

    [Fact]
    public async Task RenderedSectionWithoutMatchesIsHiddenOnTheServer()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient(favorites: ["nginx.service"]));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=backup");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        var favoritesSection = html[html.IndexOf("<section class=\"service-section\"", StringComparison.Ordinal)..];
        favoritesSection = favoritesSection[..favoritesSection.IndexOf("</section>", StringComparison.Ordinal)];
        Assert.Contains("hidden", favoritesSection[..favoritesSection.IndexOf('>', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("Wyświetlono 1 z 3 usług", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleRequiresAntiForgeryAndRedirectsOnlyToTheList()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", WebTestSupport.SessionCookie);
        var html = await client.GetStringAsync("/Services?q=ngi&state=active", TestContext.Current.CancellationToken);
        var token = WebTestSupport.AntiForgeryToken(html);
        Assert.Contains("action=\"/Services?handler=Favorite\"", html, StringComparison.Ordinal);

        using var forged = await client.PostAsync("/Services?handler=Favorite", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["service"] = "nginx.service", ["action"] = "add" }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.DoesNotContain(factory.Agent.Requests, agentRequest => agentRequest is AgentRequest.AddFavorite);

        using var valid = await client.PostAsync("/Services?handler=Favorite", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["service"] = "nginx.service",
                ["action"] = "add",
                ["q"] = "ngi",
                ["state"] = "active",
                ["returnUrl"] = "https://evil.example/",
            }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Equal("/Services?q=ngi&state=active", valid.Headers.Location?.OriginalString);
        Assert.Single(factory.Agent.Requests, agentRequest => agentRequest is AgentRequest.AddFavorite);
        Assert.Contains("nginx.service", factory.Agent.FavoriteIds);
    }

    [Fact]
    public async Task LimitRedirectCarriesOnlyTheFixedStatusAndRendersTheLimitMessage()
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.AddFavorite => new AgentResult.AddFavorite(AgentResultCode.LimitReached),
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Success, [], []),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", WebTestSupport.SessionCookie);
        var token = WebTestSupport.AntiForgeryToken(
            await client.GetStringAsync("/Services", TestContext.Current.CancellationToken));

        using var limited = await client.PostAsync("/Services?handler=Favorite", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["service"] = "nginx.service",
                ["action"] = "add",
            }), TestContext.Current.CancellationToken);

        Assert.Equal("/Services?favorite=limit", limited.Headers.Location?.OriginalString);
        var page = await client.GetStringAsync(limited.Headers.Location!.OriginalString, TestContext.Current.CancellationToken);
        Assert.Contains("Osiągnięto limit 100 ulubionych usług", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static FakeClient ListClient(string[]? favorites) =>
        new(_ => new AgentResult.ListServices(AgentResultCode.Success, Inventory, favorites));

    private static T Attach<T>(T page, HttpContext context) where T : PageModel
    {
        page.PageContext = new PageContext { HttpContext = context };
        return page;
    }

    private static DefaultHttpContext Authenticated(IFormCollection? form = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "__Host-ServalSession=opaque-token";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = form ?? new FormCollection([]);
        return context;
    }

    private static FormCollection Form(params (string Name, string Value)[] fields)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            values[name] = values.TryGetValue(name, out var existing)
                ? StringValues.Concat(existing, value) : new StringValues(value);
        }

        if (!values.ContainsKey("service"))
        {
            values["service"] = "alpha.service";
        }

        return new FormCollection(values);
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
