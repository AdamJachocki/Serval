using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serval.Application.Ipc;
using Serval.Web;
using Xunit;

namespace Serval.Web.Tests;

public sealed partial class SessionPagesTests
{
    [Fact]
    public async Task LoginCreatesOneProtectedMinimalSessionTicket()
    {
        await using var fixture = new Fixture();
        var login = await fixture.LoginAsync("submitted-name", "/Services");

        Assert.Equal(HttpStatusCode.Redirect, login.Response.StatusCode);
        Assert.Equal("/Services", login.Response.Headers.Location?.OriginalString);
        var authCookies = login.Response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal))
            .ToArray();
        var cookie = Assert.Single(authCookies);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Fixture.Session, cookie, StringComparison.Ordinal);

        var options = fixture.Factory.Services.GetRequiredService<
            IOptionsMonitor<CookieAuthenticationOptions>>().Get(ServalAuthentication.Scheme);
        Assert.Equal(TimeSpan.FromDays(1), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        var ticket = options.TicketDataFormat.Unprotect(login.AuthenticationCookie.Value);
        Assert.NotNull(ticket);
        Assert.False(ticket.Properties.IsPersistent);
        Assert.Equal(Fixture.Session, ticket.Properties.Items[ServalAuthentication.SessionProperty]);
        Assert.Equal("confirmed-user", ticket.Principal.Identity?.Name);
        Assert.Equal("1001", ticket.Principal.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(2, ticket.Principal.Claims.Count());
        Assert.DoesNotContain(ticket.Principal.Claims, claim => claim.Value == Fixture.Session);
    }

    [Fact]
    public async Task ProtectedPagesValidateEveryRequestAndRenderEncodedIdentity()
    {
        await using var fixture = new Fixture("confirmed<script>");
        var login = await fixture.LoginAsync("submitted-name");

        using var first = await fixture.GetAsync("/Services", login.AllCookies);
        using var second = await fixture.GetAsync(
            "/Services/Details?service=visible.service", login.AllCookies);
        using var third = await fixture.GetAsync("/Grants", login.AllCookies);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        foreach (var response in new[] { first, second, third })
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.NoCache);
        }
        var markup = await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("confirmed&lt;script&gt;", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmed<script>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.Session, markup, StringComparison.Ordinal);
        Assert.Equal(3, fixture.Agent.ValidationCalls);
    }

    [Fact]
    public async Task SlidingRenewalRebuildsTicketWithoutInjectedPrivilegeClaims()
    {
        await using var fixture = new Fixture();
        using var warmup = await fixture.GetAsync("/Login", null);
        var options = fixture.Factory.Services.GetRequiredService<
            IOptionsMonitor<CookieAuthenticationOptions>>().Get(ServalAuthentication.Scheme);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "confirmed-user"),
            new Claim(ClaimTypes.NameIdentifier, "1001"),
            new Claim(ClaimTypes.Role, "root"),
        };
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(13)),
            ExpiresUtc = DateTimeOffset.UtcNow.Add(TimeSpan.FromHours(11)),
            IsPersistent = false,
            AllowRefresh = true,
        };
        properties.Items[ServalAuthentication.SessionProperty] = Fixture.Session;
        var injected = options.TicketDataFormat.Protect(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, ServalAuthentication.Scheme)),
            properties, ServalAuthentication.Scheme));

        using var response = await fixture.GetAsync("/Services",
            $"{ServalAuthentication.CookieName}={injected}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renewedHeader = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal));
        var renewedValue = renewedHeader.Split(';', 2)[0].Split('=', 2)[1];
        var renewed = options.TicketDataFormat.Unprotect(renewedValue);
        Assert.NotNull(renewed);
        Assert.Equal(2, renewed.Principal.Claims.Count());
        Assert.DoesNotContain(renewed.Principal.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.Equal(Fixture.Session,
            renewed.Properties.Items[ServalAuthentication.SessionProperty]);
        Assert.Equal(1, fixture.Agent.ValidationCalls);
    }

    [Fact]
    public async Task InvalidUnavailableAndLegacyTicketsChallengeWithoutLoginLoop()
    {
        await using var fixture = new Fixture();
        using var anonymous = await fixture.GetAsync("/Services", null);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Login?ReturnUrl=", anonymous.Headers.Location?.PathAndQuery,
            StringComparison.OrdinalIgnoreCase);

        using var legacy = await fixture.GetAsync("/Services",
            $"{ServalAuthentication.CookieName}=opaque-token");
        Assert.Equal(HttpStatusCode.Redirect, legacy.StatusCode);
        Assert.Equal(0, fixture.Agent.ValidationCalls);

        var login = await fixture.LoginAsync("alice");
        fixture.Agent.ValidationCode = AgentResultCode.Unavailable;
        using var unavailable = await fixture.GetAsync("/Services", login.AllCookies);
        Assert.Equal(HttpStatusCode.Redirect, unavailable.StatusCode);
        Assert.StartsWith("/Login?ReturnUrl=", unavailable.Headers.Location?.PathAndQuery,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(unavailable.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal));

        using var reachableLogin = await fixture.GetAsync("/Login", null);
        Assert.Equal(HttpStatusCode.OK, reachableLogin.StatusCode);
    }

    [Fact]
    public async Task MismatchedConfirmedIdentityAndOperationInvalidationClearAuthentication()
    {
        await using var fixture = new Fixture();
        var login = await fixture.LoginAsync("alice");

        fixture.Agent.ValidationName = "replacement-user";
        using var mismatch = await fixture.GetAsync("/Services", login.AllCookies);
        Assert.Equal(HttpStatusCode.Redirect, mismatch.StatusCode);
        Assert.Contains(mismatch.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal));

        fixture.Agent.ValidationName = "confirmed-user";
        fixture.Agent.OperationCode = AgentResultCode.Unauthenticated;
        using var operationRace = await fixture.GetAsync("/Services", login.AllCookies);
        Assert.Equal(HttpStatusCode.Redirect, operationRace.StatusCode);
        Assert.Contains(operationRace.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal));
        login.Response.Dispose();
    }

    [Fact]
    public async Task LoginDistinguishesAvailabilityAndRejectsExternalReturnUrlWithoutSecrets()
    {
        await using var fixture = new Fixture();
        var successful = await fixture.LoginAsync("alice", "https://attacker.example/");
        Assert.Equal("/Services", successful.Response.Headers.Location?.OriginalString);
        successful.Response.Dispose();

        fixture.Agent.LoginCode = AgentResultCode.Denied;
        var denied = await fixture.LoginAsync("alice", null, "wrong-password-marker");
        var deniedMarkup = await denied.Response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, denied.Response.StatusCode);
        Assert.Contains("Sign in failed. Check your credentials.", deniedMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-password-marker", deniedMarkup, StringComparison.Ordinal);
        Assert.False(denied.Response.Headers.TryGetValues("Set-Cookie", out var deniedCookies) &&
            deniedCookies.Any(value => value.StartsWith(
                ServalAuthentication.CookieName + "=", StringComparison.Ordinal)));
        denied.Response.Dispose();

        fixture.Agent.LoginCode = AgentResultCode.Unavailable;
        var unavailable = await fixture.LoginAsync("alice");
        var unavailableMarkup = await unavailable.Response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.Contains("Sign in is temporarily unavailable. Try again later.", unavailableMarkup,
            StringComparison.Ordinal);
        unavailable.Response.Dispose();
    }

    [Fact]
    public async Task LogoutRequiresAntiforgeryAndAcknowledgedRevocation()
    {
        await using var fixture = new Fixture();
        var login = await fixture.LoginAsync("alice");

        using var missingProof = new HttpRequestMessage(HttpMethod.Post, "/Logout");
        missingProof.Headers.Add("Cookie", login.AllCookies);
        using var rejected = await fixture.Client.SendAsync(missingProof, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(0, fixture.Agent.LogoutCalls);

        using var services = await fixture.GetAsync("/Services", login.AllCookies);
        var markup = await services.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var antiforgery = ExtractAntiforgery(markup);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Logout")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgery,
            }),
        };
        request.Headers.Add("Cookie", login.AllCookies);
        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, fixture.Agent.LogoutCalls);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(ServalAuthentication.CookieName + "=", StringComparison.Ordinal));

        using var replay = await fixture.GetAsync("/Services", login.AllCookies);
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        login.Response.Dispose();
    }

    [Fact]
    public async Task LogoutCommunicationFailureDoesNotClaimRemoteRevocation()
    {
        await using var fixture = new Fixture();
        var login = await fixture.LoginAsync("alice");
        fixture.Agent.LogoutCode = AgentResultCode.Unavailable;
        using var services = await fixture.GetAsync("/Services", login.AllCookies);
        var antiforgery = ExtractAntiforgery(await services.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Logout")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgery,
            }),
        };
        request.Headers.Add("Cookie", login.AllCookies);

        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign out could not be confirmed. Try again.",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith(
                ServalAuthentication.CookieName + "=", StringComparison.Ordinal)));
        using var stillAuthenticated = await fixture.GetAsync("/Services", login.AllCookies);
        Assert.Equal(HttpStatusCode.OK, stillAuthenticated.StatusCode);
        login.Response.Dispose();
    }

    [Fact]
    public async Task LoginWithoutAntiforgeryProofNeverCallsAgent()
    {
        await using var fixture = new Fixture();
        using var response = await fixture.Client.PostAsync("/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = "alice",
                ["Password"] = "secret",
            }), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Agent.LoginCalls);
    }

    [Fact]
    public async Task GrantMutationWithoutAntiforgeryProofNeverCallsAgent()
    {
        await using var fixture = new Fixture();
        var login = await fixture.LoginAsync("alice");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Grants?handler=Add")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["SubjectKind"] = "User",
                ["Subject"] = "alice",
                ["Service"] = "visible.service",
            }),
        };
        request.Headers.Add("Cookie", login.AllCookies);
        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Agent.AddGrantCalls);
        login.Response.Dispose();
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryRegex();

    private static string ExtractAntiforgery(string markup) =>
        WebUtility.HtmlDecode(Assert.Single(AntiforgeryRegex().Matches(markup)).Groups[1].Value);

    private sealed class Fixture : IAsyncDisposable
    {
        public const string Session = "abcdefghijklmnopqrstuvwxyzABCDEFGH012345678";

        public Fixture(string confirmedName = "confirmed-user")
        {
            Agent = new StatefulAgent(confirmedName);
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IAgentIpcClient>(Agent));
            });
            Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
                BaseAddress = new Uri("https://localhost"),
            });
        }

        public StatefulAgent Agent { get; }
        public WebApplicationFactory<Program> Factory { get; }
        public HttpClient Client { get; }

        public async Task<LoginResponse> LoginAsync(
            string username, string? returnUrl = null, string password = "synthetic-password")
        {
            using var get = await Client.GetAsync("/Login", TestContext.Current.CancellationToken);
            var markup = await get.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var antiforgery = ExtractAntiforgery(markup);
            var getCookies = CookiePairs(get);
            var form = new Dictionary<string, string>
            {
                ["Username"] = username,
                ["Password"] = password,
                ["__RequestVerificationToken"] = antiforgery,
            };
            if (returnUrl is not null)
            {
                form["ReturnUrl"] = returnUrl;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "/Login")
            {
                Content = new FormUrlEncodedContent(form),
            };
            request.Headers.Add("Cookie", getCookies);
            var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
            var authCookie = CookiePairs(response).Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .SingleOrDefault(value => value.StartsWith(
                    ServalAuthentication.CookieName + "=", StringComparison.Ordinal));
            var allCookies = string.Join("; ", new[] { getCookies, authCookie }
                .Where(value => !string.IsNullOrEmpty(value)));
            var pair = authCookie?.Split('=', 2);
            return new LoginResponse(response,
                pair is null ? default : new KeyValuePair<string, string>(pair[0], pair[1]),
                allCookies);
        }

        public async Task<HttpResponseMessage> GetAsync(string path, string? cookies)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (!string.IsNullOrEmpty(cookies))
            {
                request.Headers.Add("Cookie", cookies);
            }

            return await Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        private static string CookiePairs(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Set-Cookie", out var values)
                ? string.Join("; ", values.Select(value => value.Split(';', 2)[0]))
                : string.Empty;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private sealed record LoginResponse(
        HttpResponseMessage Response,
        KeyValuePair<string, string> AuthenticationCookie,
        string AllCookies);

    private sealed class StatefulAgent : IAgentIpcClient
    {
        private bool valid = true;
        private readonly string confirmedName;

        public StatefulAgent(string confirmedName)
        {
            this.confirmedName = confirmedName;
            ValidationName = confirmedName;
        }

        public AgentResultCode LoginCode { get; set; } = AgentResultCode.Success;
        public AgentResultCode ValidationCode { get; set; } = AgentResultCode.Success;
        public AgentResultCode OperationCode { get; set; } = AgentResultCode.Success;
        public AgentResultCode LogoutCode { get; set; } = AgentResultCode.Success;
        public string ValidationName { get; set; }
        public int LoginCalls { get; private set; }
        public int ValidationCalls { get; private set; }
        public int LogoutCalls { get; private set; }
        public int AddGrantCalls { get; private set; }

        public Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
        {
            AgentResult result = request switch
            {
                AgentRequest.Login => Login(),
                AgentRequest.ValidateSession validation => Validate(validation),
                AgentRequest.ListServices list when valid && list.Session == Fixture.Session =>
                    new AgentResult.ListServices(OperationCode,
                        OperationCode == AgentResultCode.Success ? [] : null),
                AgentRequest.InspectService inspect when valid && inspect.Session == Fixture.Session =>
                    new AgentResult.InspectService(AgentResultCode.Success,
                        new AgentService("visible.service", "Visible", "loaded", "active", "running")),
                AgentRequest.ListGrants grants when valid && grants.Session == Fixture.Session =>
                    new AgentResult.ListGrants(AgentResultCode.Success, []),
                AgentRequest.AddGrant => AddGrant(),
                AgentRequest.Logout logout => Logout(logout),
                _ => new AgentResult.ListServices(AgentResultCode.Unauthenticated, null),
            };
            return Task.FromResult(result);
        }

        private AgentResult.Login Login()
        {
            LoginCalls++;
            valid = LoginCode == AgentResultCode.Success;
            return new AgentResult.Login(LoginCode,
                LoginCode == AgentResultCode.Success ? Fixture.Session : null,
                LoginCode == AgentResultCode.Success ? new AgentIdentity(confirmedName, 1001) : null);
        }

        private AgentResult.ValidateSession Validate(AgentRequest.ValidateSession request)
        {
            ValidationCalls++;
            var success = valid && request.Session == Fixture.Session &&
                ValidationCode == AgentResultCode.Success;
            return new AgentResult.ValidateSession(success ? AgentResultCode.Success : ValidationCode,
                success ? new AgentIdentity(ValidationName, 1001) : null);
        }

        private AgentResult.AddGrant AddGrant()
        {
            AddGrantCalls++;
            return new AgentResult.AddGrant(AgentResultCode.Success, 1);
        }

        private AgentResult.Logout Logout(AgentRequest.Logout request)
        {
            LogoutCalls++;
            if (LogoutCode != AgentResultCode.Success)
            {
                return new AgentResult.Logout(LogoutCode);
            }

            if (!valid || request.Session != Fixture.Session)
            {
                return new AgentResult.Logout(AgentResultCode.Unauthenticated);
            }

            valid = false;
            return new AgentResult.Logout(AgentResultCode.Success);
        }
    }
}
