using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Web.Tests;

public sealed class LoopbackHttpLoginTests
{
    // Firefox coverage is unverified here: only Chromium is installed by the repository's CI step.
    [Fact]
    public async Task LoginOverLoopbackHttpStoresTheSecureSessionCookie()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient(
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        factory.UseKestrel(0);
        factory.StartServer();
        var published = new Uri(factory.ClientOptions.BaseAddress.ToString());
        Assert.Equal("http", published.Scheme);
        var baseAddress = new UriBuilder(published) { Host = "localhost" }.Uri;

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(new Uri(baseAddress, "/Login").ToString());
        await page.Locator("#Username").FillAsync("alice");
        await page.Locator("#Password").FillAsync("synthetic-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Zaloguj się" }).ClickAsync();
        await page.WaitForURLAsync("**/Services");

        var cookie = Assert.Single(await context.CookiesAsync([baseAddress.ToString()]),
            candidate => candidate.Name == ServalSessionCookie.Name);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal("opaque-token", cookie.Value);
    }
}
