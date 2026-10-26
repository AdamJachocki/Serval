using System.Net;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Web.Tests;

public sealed class RenderedWebTests
{
    [Fact]
    public async Task LoginUsesPolishSimpleShellAndOnlyLocalAssets()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);

        using var response = await client.GetAsync("/Login", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"pl\">", html, StringComparison.Ordinal);
        Assert.Contains("Zaloguj się", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"current-password\"", html, StringComparison.Ordinal);
        Assert.Contains("/assets/login-architecture.png", await client.GetStringAsync("/css/site.css", TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.DoesNotContain("Nawigacja główna", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The Hasło field is required.", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/login-architecture.png", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/fonts/space-grotesk-latin-ext.woff2", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/fonts/jetbrains-mono-latin-ext.woff2", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task LoginValidationIsPolishAndNeverReflectsPassword()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        var html = await client.GetStringAsync("/Login", TestContext.Current.CancellationToken);
        var token = WebTestSupport.AntiForgeryToken(html);
        const string secret = "synthetic-password-do-not-reflect";

        using var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = string.Empty,
            ["Password"] = secret,
        }), TestContext.Current.CancellationToken);
        var returned = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var decoded = WebUtility.HtmlDecode(returned);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Podaj nazwę użytkownika.", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, returned, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Agent.Requests, request => request is AgentRequest.Login);
    }

    [Fact]
    public async Task RejectedLoginIsGenericAndInvalidAntiForgeryIsRejected()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        var token = WebTestSupport.AntiForgeryToken(await client.GetStringAsync("/Login", TestContext.Current.CancellationToken));
        const string secret = "wrong-and-private";

        using var rejected = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = "alice",
            ["Password"] = secret,
        }), TestContext.Current.CancellationToken);
        var html = await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Nie udało się zalogować. Sprawdź dane i spróbuj ponownie.", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, html, StringComparison.Ordinal);

        using var invalidToken = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "alice",
            ["Password"] = "irrelevant",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidToken.StatusCode);
    }

    [Fact]
    public async Task EmptyPasswordStillDelegatesAndSuccessfulLoginKeepsRedirectContract()
    {
        await using var deniedFactory = new ServalWebFactory(new TestAgentClient());
        using var deniedClient = WebTestSupport.CreateClient(deniedFactory);
        var deniedToken = WebTestSupport.AntiForgeryToken(await deniedClient.GetStringAsync("/Login", TestContext.Current.CancellationToken));
        using var denied = await deniedClient.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = deniedToken,
            ["Username"] = "alice",
            ["Password"] = string.Empty,
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        var login = Assert.IsType<AgentRequest.Login>(Assert.Single(deniedFactory.Agent.Requests));
        Assert.Equal(string.Empty, login.Password);

        await using var successFactory = new ServalWebFactory(new TestAgentClient(
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        using var successClient = WebTestSupport.CreateClient(successFactory);
        var successToken = WebTestSupport.AntiForgeryToken(await successClient.GetStringAsync("/Login", TestContext.Current.CancellationToken));
        using var success = await successClient.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = successToken,
            ["Username"] = "alice",
            ["Password"] = "synthetic-password",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.Equal("/Services", success.Headers.Location?.OriginalString);
        Assert.Contains("__Host-ServalSession=opaque-token", success.Headers.GetValues("Set-Cookie").Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(129, 1)]
    [InlineData(1, 1025)]
    public async Task OverlongLoginInputIsRejectedWithoutAgentOrPasswordReflection(int usernameLength, int passwordLength)
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        var token = WebTestSupport.AntiForgeryToken(await client.GetStringAsync("/Login", TestContext.Current.CancellationToken));
        var password = new string('p', passwordLength);

        using var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Username"] = new string('u', usernameLength),
            ["Password"] = password,
        }), TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(factory.Agent.Requests);
        if (passwordLength > 1)
        {
            Assert.Contains("Hasło może mieć najwyżej 1024 znaki.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(password, html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("Nazwa użytkownika może mieć najwyżej 128 znaków.", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task InventoryEncodesMetadataMapsStatesAndAppliesCombinedFilters()
    {
        var services = new AgentService[]
        {
            new("active.service", "Alpha <script>alert(1)</script>", "loaded", "active", "running"),
            new("inactive.service", string.Empty, "loaded", "inactive", "dead"),
            new("failed.service", "Backup", "loaded", "failed", "failed"),
            new("activating.service", "Alpha worker", "loaded", "activating", "start"),
            new("deactivating.service", "Worker", "loaded", "deactivating", "stop"),
            new("reloading.service", "Worker", "loaded", "reloading", "reload"),
            new("other.service", "Worker", "loaded", "maintenance", "waiting"),
            new("missing.service", "Worker", "loaded", string.Empty, "waiting"),
        };
        await using var factory = new ServalWebFactory(new TestAgentClient(services));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=alpha&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Wyświetlono 1 z 8 usług", html, StringComparison.Ordinal);
        Assert.Contains("Aktywna", decoded, StringComparison.Ordinal);
        Assert.Contains("Nieaktywna", decoded, StringComparison.Ordinal);
        Assert.Contains("Błąd", decoded, StringComparison.Ordinal);
        Assert.Contains("Aktywacja", decoded, StringComparison.Ordinal);
        Assert.Contains("Dezaktywacja", decoded, StringComparison.Ordinal);
        Assert.Contains("Przeładowanie", decoded, StringComparison.Ordinal);
        Assert.Contains("maintenance", html, StringComparison.Ordinal);
        Assert.Contains("Nieznany stan", decoded, StringComparison.Ordinal);
        Assert.Contains("Brak opisu", decoded, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InventoryPreservesAuthorizedOrderAndCompleteLongIdentifiers()
    {
        var longIdentifier = $"{new string('a', 120)}.service";
        var services = new AgentService[]
        {
            new("first.service", "Pierwsza", "loaded", "active", "running"),
            new(longIdentifier, "Długi identyfikator", "loaded", "inactive", "dead"),
            new("last.service", "Ostatnia", "loaded", "failed", "failed"),
        };
        await using var factory = new ServalWebFactory(new TestAgentClient(services));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(longIdentifier, html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("first.service", StringComparison.Ordinal) < html.IndexOf(longIdentifier, StringComparison.Ordinal));
        Assert.True(html.IndexOf(longIdentifier, StringComparison.Ordinal) < html.IndexOf("last.service", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Services?q=one&q=two")]
    [InlineData("/Services?state=active&state=failed")]
    public async Task RepeatedFilterIsRejectedAfterAuthorizedInventory(string path)
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet(path);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(factory.Agent.Requests, agentRequest => agentRequest is AgentRequest.ListServices);
    }

    [Theory]
    [InlineData("/Services?q=one&q=two")]
    [InlineData("/Services?state=active&state=failed")]
    [InlineData("/Services/Details?service=temporary.service&q=one&q=two")]
    [InlineData("/Services/Details?service=temporary.service&state=active&state=failed")]
    public async Task InvalidFiltersRemainBadRequestWhenAgentIsUnavailable(string path)
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Unavailable, null),
            AgentRequest.InspectService => new AgentResult.InspectService(AgentResultCode.Unavailable, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet(path);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Nieprawidłowe parametry filtrowania.", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("one", html, StringComparison.Ordinal);
        Assert.DoesNotContain("active&amp;state=failed", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlongFiltersRemainBadRequestWhenAgentIsUnavailable()
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Unavailable, null),
            AgentRequest.InspectService => new AgentResult.InspectService(AgentResultCode.Unavailable, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        var paths = new[]
        {
            $"/Services?q={new string('x', 257)}",
            $"/Services/Details?service=temporary.service&state={new string('y', 65)}",
        };

        foreach (var path in paths)
        {
            using var request = WebTestSupport.AuthenticatedGet(path);
            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Nieprawidłowe parametry filtrowania.", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.DoesNotContain(new string('x', 257), html, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('y', 65), html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OverlongAndAbsentFilterValuesStayBoundedAndExplicit()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        using var overlongRequest = WebTestSupport.AuthenticatedGet($"/Services?q={new string('x', 257)}");
        using var overlong = await client.SendAsync(overlongRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);

        using var absentRequest = WebTestSupport.AuthenticatedGet("/Services?state=maintenance");
        using var absent = await client.SendAsync(absentRequest, TestContext.Current.CancellationToken);
        var html = await absent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
        Assert.Contains("Wyświetlono 0 z 3 usług", html, StringComparison.Ordinal);
        Assert.Contains("value=\"maintenance\" selected", html, StringComparison.Ordinal);
        Assert.Contains("Brak wyników.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulEmptyInventoryIsDistinctFromFailure()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient([]));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Brak usług do wyświetlenia.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lista usług jest chwilowo niedostępna.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextFilterDoesNotMatchAcrossIdentifierAndDescriptionBoundary()
    {
        var services = new[] { new AgentService("bridge.service", "Beta", "loaded", "active", "running") };
        await using var factory = new ServalWebFactory(new TestAgentClient(services));
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=service%20b");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Wyświetlono 0 z 1 usług", html, StringComparison.Ordinal);
        Assert.Contains("Brak wyników.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousAndExpiredInventoryNeverExposeRows()
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Unauthenticated, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);

        using var anonymous = await client.GetAsync("/Services", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Empty(agent.Requests);

        using var request = WebTestSupport.AuthenticatedGet("/Services");
        using var expired = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var expiredHtml = await expired.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
        Assert.DoesNotContain("nginx.service", expiredHtml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentResultCode.NotFound)]
    [InlineData(AgentResultCode.Denied)]
    [InlineData(AgentResultCode.InvalidRequest)]
    public async Task HiddenInvalidAndDeniedDetailsAreIndistinguishable(AgentResultCode code)
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.InspectService => new AgentResult.InspectService(code, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services/Details?service=hidden.service&q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DetailsUseCanonicalResultAndOnlyFixedFilterPreservingLinks()
    {
        var canonical = new AgentService("canonical.service", "Canonical", "loaded", "active", "running");
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.InspectService => new AgentResult.InspectService(AgentResultCode.Success, canonical),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services/Details?service=alias.service&q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("canonical.service", html, StringComparison.Ordinal);
        Assert.Contains("/Services?q=web&amp;state=active", html, StringComparison.Ordinal);
        Assert.Contains("service=canonical.service&amp;q=web&amp;state=active", html, StringComparison.Ordinal);
        Assert.DoesNotContain("returnUrl", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InventoryFailureContainsRetryButNoStaleRows()
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.ListServices => new AgentResult.ListServices(AgentResultCode.Unavailable, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services?q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Lista usług jest chwilowo niedostępna.", html, StringComparison.Ordinal);
        Assert.Contains("Ponów", html, StringComparison.Ordinal);
        Assert.Contains("/Services?q=web&state=active", html, StringComparison.Ordinal);
        Assert.DoesNotContain("nginx.service", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Wyświetlono 0", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailFailureOffersFixedRetryAndReturnWithoutStaleMetadata()
    {
        var agent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.InspectService => new AgentResult.InspectService(AgentResultCode.Unavailable, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var factory = new ServalWebFactory(agent);
        using var client = WebTestSupport.CreateClient(factory);
        using var request = WebTestSupport.AuthenticatedGet("/Services/Details?service=temporary.service&q=web&state=active");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Szczegóły usługi są chwilowo niedostępne.", html, StringComparison.Ordinal);
        Assert.Contains("Ponów", html, StringComparison.Ordinal);
        Assert.Contains("Wróć do usług", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Nazwa kanoniczna", html, StringComparison.Ordinal);
        Assert.DoesNotContain("returnUrl", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplicationShellUsesPostLogoutWithAntiForgery()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", WebTestSupport.SessionCookie);
        var html = await client.GetStringAsync("/Services", TestContext.Current.CancellationToken);
        var token = WebTestSupport.AntiForgeryToken(html);

        Assert.Contains("action=\"/Logout\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains("Przejdź do treści", html, StringComparison.Ordinal);

        using var missingToken = await client.PostAsync("/Logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);

        using var valid = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.Contains(factory.Agent.Requests, request => request is AgentRequest.Logout);
    }

    [Fact]
    public async Task GrantsPreserveDenialAndEncodeAgentFields()
    {
        var hostileGrant = new AgentGrant(1, "User", "<img src=x onerror=alert(1)>", "Service.View", "visible.service");
        await using var permittedFactory = new ServalWebFactory(new TestAgentClient(grants: [hostileGrant]));
        using var permittedClient = WebTestSupport.CreateClient(permittedFactory);
        using var permittedRequest = WebTestSupport.AuthenticatedGet("/Grants");
        using var permitted = await permittedClient.SendAsync(permittedRequest, TestContext.Current.CancellationToken);
        var permittedHtml = await permitted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", permittedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", permittedHtml, StringComparison.Ordinal);

        var deniedAgent = new TestAgentClient(handler: (request, _) => Task.FromResult<AgentResult>(request switch
        {
            AgentRequest.ListGrants => new AgentResult.ListGrants(AgentResultCode.Denied, null),
            _ => throw new InvalidOperationException(),
        }));
        await using var deniedFactory = new ServalWebFactory(deniedAgent);
        using var deniedClient = WebTestSupport.CreateClient(deniedFactory);
        using var deniedRequest = WebTestSupport.AuthenticatedGet("/Grants");
        using var denied = await deniedClient.SendAsync(deniedRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task GrantMutationsRemainAntiForgedAgentOperations()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient());
        using var client = WebTestSupport.CreateClient(factory);
        client.DefaultRequestHeaders.Add("Cookie", WebTestSupport.SessionCookie);
        var token = WebTestSupport.AntiForgeryToken(await client.GetStringAsync("/Grants", TestContext.Current.CancellationToken));

        using var invalid = await client.PostAsync("/Grants?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SubjectKind"] = "User",
            ["Subject"] = "alice",
            ["Service"] = "nginx.service",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var added = await client.PostAsync("/Grants?handler=Add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["SubjectKind"] = "User",
            ["Subject"] = "alice",
            ["Service"] = "nginx.service",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        using var removed = await client.PostAsync("/Grants?handler=Remove", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["GrantId"] = "1",
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Contains(factory.Agent.Requests, request => request is AgentRequest.AddGrant);
        Assert.Contains(factory.Agent.Requests, request => request is AgentRequest.RemoveGrant);
    }
}
