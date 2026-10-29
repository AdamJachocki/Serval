using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Playwright;
using Serval.Application.Ipc;
using Xunit;

namespace Serval.Web.Tests;

public sealed class BrowserInteractionTests
{
    [Fact]
    public async Task LoginIsLocalResponsiveAndPreventsDuplicateSubmission()
    {
        var agent = new TestAgentClient(handler: async (request, cancellationToken) => request switch
        {
            AgentRequest.Login => await RejectLoginAfterDelay(cancellationToken),
            _ => throw new InvalidOperationException(),
        });
        await using var factory = new ServalWebFactory(agent);
        var baseAddress = StartLoopback(factory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1536, Height = 1024 },
            ReducedMotion = ReducedMotion.Reduce,
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        var requests = new ConcurrentQueue<Uri>();
        page.Request += (_, request) => requests.Enqueue(new Uri(request.Url));

        await page.GotoAsync(new Uri(baseAddress, "/Login").ToString());
        Assert.True(await page.EvaluateAsync<bool>("() => Promise.all([document.fonts.load('16px \\\"Space Grotesk\\\"', 'Serval'), document.fonts.load('16px \\\"JetBrains Mono\\\"', 'nginx.service')]).then(results => results.every(faces => faces.length > 0))"));
        var transitionDurations = await page.GetByRole(AriaRole.Button, new() { Name = "Zaloguj się" })
            .EvaluateAsync<string>("element => getComputedStyle(element).transitionDuration");
        Assert.All(transitionDurations.Split(',', StringSplitOptions.TrimEntries), duration => Assert.Equal("0s", duration));
        await page.GetByLabel("Nazwa użytkownika").FocusAsync();
        Assert.NotEqual("none", await page.GetByLabel("Nazwa użytkownika")
            .EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        await page.ScreenshotAsync(new() { Path = EvidencePath("login-1536x1024.png"), FullPage = true });
        await page.GetByLabel("Nazwa użytkownika").FillAsync("alice");
        await page.GetByLabel("Hasło", new() { Exact = true }).FillAsync("synthetic-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Pokaż hasło" }).ClickAsync();
        Assert.Equal("text", await page.GetByLabel("Hasło", new() { Exact = true }).GetAttributeAsync("type"));
        Assert.Equal("synthetic-password", await page.GetByLabel("Hasło", new() { Exact = true }).InputValueAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Ukryj hasło" }).ClickAsync();
        Assert.Equal("password", await page.GetByLabel("Hasło", new() { Exact = true }).GetAttributeAsync("type"));

        await page.Locator("[data-login-form]").EvaluateAsync("form => { form.requestSubmit(); form.requestSubmit(); }");
        await page.GetByText("Nie udało się zalogować. Sprawdź dane i spróbuj ponownie.").WaitForAsync();
        Assert.Single(agent.Requests, request => request is AgentRequest.Login);
        Assert.Equal(string.Empty, await page.GetByLabel("Hasło", new() { Exact = true }).InputValueAsync());

        Assert.All(requests, request => Assert.Equal(baseAddress.Authority, request.Authority));

        await page.SetViewportSizeAsync(320, 800);
        Assert.Equal("none", await page.Locator(".login-art").EvaluateAsync<string>("element => getComputedStyle(element).display"));
        Assert.False(await HasDocumentOverflow(page));
    }

    [Fact]
    public async Task ServiceFilteringDetailsAndResponsiveShellWorkInBrowser()
    {
        var services = new AgentService[]
        {
            new("nginx.service", "Serwer WWW", "loaded", "active", "running"),
            new("postgresql.service", "Baza danych", "loaded", "active", "running"),
            new("redis.service", "Pamięć podręczna", "loaded", "active", "running"),
            new("backup.service", "Kopia zapasowa", "loaded", "failed", "failed"),
            new("worker.service", "Zadania w tle", "loaded", "inactive", "dead"),
            new("api.service", "ŻÓŁĆ API", "loaded", "active", "running"),
        };
        await using var factory = new ServalWebFactory(new TestAgentClient(
            services,
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        var baseAddress = StartLoopback(factory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new ViewportSize { Width = 1536, Height = 1024 },
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await SignInAsync(page, baseAddress);

        Assert.Equal(5, await page.GetByRole(AriaRole.Columnheader).CountAsync());
        await page.GetByPlaceholder("Szukaj po nazwie lub opisie").FillAsync("żółć");
        await page.GetByText("Wyświetlono 1 z 6 usług").WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "api.service" }).IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Wyczyść filtry" }).ClickAsync();

        await page.GetByPlaceholder("Szukaj po nazwie lub opisie").FillAsync("backup");
        await page.GetByLabel("Stan usługi").SelectOptionAsync("failed");
        await page.GetByText("Wyświetlono 1 z 6 usług").WaitForAsync();
        Assert.Contains("q=backup", page.Url, StringComparison.Ordinal);
        Assert.Contains("state=failed", page.Url, StringComparison.Ordinal);
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "backup.service" }).IsVisibleAsync());
        Assert.False(await page.GetByRole(AriaRole.Link, new() { Name = "nginx.service" }).IsVisibleAsync());
        await page.GetByRole(AriaRole.Link, new() { Name = "backup.service" }).ClickAsync();
        var returnLink = page.GetByRole(AriaRole.Link, new() { Name = "Wróć do usług" });
        Assert.Contains("q=backup", await returnLink.GetAttributeAsync("href"), StringComparison.Ordinal);
        Assert.Contains("state=failed", await returnLink.GetAttributeAsync("href"), StringComparison.Ordinal);
        await returnLink.ClickAsync();
        await page.GetByText("Wyświetlono 1 z 6 usług").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Wyczyść filtry" }).ClickAsync();
        await page.GetByText("Wyświetlono 6 z 6 usług").WaitForAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Odśwież" }).ClickAsync();
        await page.GetByText("Wyświetlono 6 z 6 usług").WaitForAsync();

        await page.EvaluateAsync("document.activeElement?.blur()");
        await page.ScreenshotAsync(new() { Path = EvidencePath("services-1536x1024.png"), FullPage = true });
        await page.GetByRole(AriaRole.Link, new() { Name = "nginx.service" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "nginx.service" }).WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "Wróć do usług" }).IsVisibleAsync());
        await page.ScreenshotAsync(new() { Path = EvidencePath("details-1536x1024.png"), FullPage = true });

        foreach (var width in new[] { 320, 360, 384, 768, 1024, 1440 })
        {
            await page.SetViewportSizeAsync(width, 900);
            Assert.False(await HasDocumentOverflow(page));

            if (width == 768)
            {
                Assert.Equal("none", await page.Locator(".sidebar").EvaluateAsync<string>("element => getComputedStyle(element).display"));
                Assert.NotEqual("none", await page.Locator(".mobile-header").EvaluateAsync<string>("element => getComputedStyle(element).display"));
            }
            else if (width == 1024)
            {
                Assert.NotEqual("none", await page.Locator(".sidebar").EvaluateAsync<string>("element => getComputedStyle(element).display"));
                Assert.Equal("none", await page.Locator(".mobile-header").EvaluateAsync<string>("element => getComputedStyle(element).display"));
            }
        }
    }

    [Fact]
    public async Task NativeNoScriptNavigationAndFilteringRemainAvailable()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient(
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        var baseAddress = StartLoopback(factory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = 360, Height = 800 },
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(baseAddress, "/Login").ToString());
        Assert.False(await page.GetByRole(AriaRole.Button, new() { Name = "Pokaż hasło" }).IsVisibleAsync());
        await SignInAsync(page, baseAddress);

        await page.GotoAsync(new Uri(baseAddress, "/Services?q=worker&state=inactive").ToString());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Filtruj" }).IsVisibleAsync());
        Assert.False(await page.GetByRole(AriaRole.Button, new() { Name = "Wyczyść filtry" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "worker.service" }).IsVisibleAsync());
        Assert.False(await page.GetByRole(AriaRole.Link, new() { Name = "nginx.service" }).IsVisibleAsync());
        await page.GetByText("Menu", new() { Exact = true }).ClickAsync();
        Assert.True(await page.Locator(".mobile-navigation").GetByRole(AriaRole.Link, new() { Name = "Uprawnienia" }).IsVisibleAsync());
        Assert.False(await HasDocumentOverflow(page));
    }

    [Fact]
    public async Task SuccessfulEmptyInventoryKeepsFallbackAndRevalidatesPersistedPage()
    {
        await using var factory = new ServalWebFactory(new TestAgentClient(
            [],
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        var baseAddress = StartLoopback(factory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, error) => errors.Enqueue(error);
        await SignInAsync(page, baseAddress);

        await page.GetByText("Brak usług do wyświetlenia.").WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Filtruj" }).IsVisibleAsync());
        Assert.Empty(errors);

        var listRequestsBeforeRestore = factory.Agent.Requests.Count(request => request is AgentRequest.ListServices);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleRequest(object? _, IRequest request)
        {
            if (new Uri(request.Url).AbsolutePath == "/Services") reloaded.TrySetResult();
        }

        page.Request += HandleRequest;
        try
        {
            await page.EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            page.Request -= HandleRequest;
        }

        await page.GetByText("Brak usług do wyświetlenia.").WaitForAsync();
        Assert.True(factory.Agent.Requests.Count(request => request is AgentRequest.ListServices) > listRequestsBeforeRestore);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ClientFilterDoesNotMatchAcrossIdentifierAndDescriptionBoundary()
    {
        var services = new[] { new AgentService("bridge.service", "Beta", "loaded", "active", "running") };
        await using var factory = new ServalWebFactory(new TestAgentClient(
            services,
            loginResult: new AgentResult.Login(AgentResultCode.Success, "opaque-token")));
        var baseAddress = StartLoopback(factory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
        var page = await context.NewPageAsync();
        await SignInAsync(page, baseAddress);

        await page.GetByPlaceholder("Szukaj po nazwie lub opisie").FillAsync("service b");
        await page.GetByText("Wyświetlono 0 z 1 usług").WaitForAsync();
        Assert.False(await page.GetByRole(AriaRole.Link, new() { Name = "bridge.service" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Heading, new() { Name = "Brak wyników." }).IsVisibleAsync());
    }

    private static Uri StartLoopback(ServalWebFactory factory)
    {
        factory.UseKestrel(0);
        factory.UseKestrel(options => options.ConfigureEndpointDefaults(listen => listen.UseHttps(TestCertificate.Value)));
        factory.StartServer();
        var published = factory.ClientOptions.BaseAddress;
        return new UriBuilder(published) { Host = "localhost" }.Uri;
    }

    private static async Task SignInAsync(IPage page, Uri baseAddress)
    {
        var response = await page.GotoAsync(new Uri(baseAddress, "/Login").ToString());
        Assert.Equal(200, response?.Status);
        await page.Locator("#Username").FillAsync("alice");
        await page.Locator("#Password").FillAsync("synthetic-password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Zaloguj się" }).ClickAsync();
        await page.WaitForURLAsync("**/Services");
    }

    private static async Task<bool> HasDocumentOverflow(IPage page) =>
        await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

    private static async Task<AgentResult> RejectLoginAfterDelay(CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);
        return new AgentResult.Login(AgentResultCode.Denied, null);
    }

    private static string EvidencePath(string fileName)
    {
        var directory = Path.Combine(RepositoryRoot(), ".artifacts", "obsidian-ui");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Serval.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static readonly Lazy<X509Certificate2> TestCertificate = new(CreateTestCertificate);

    private static X509Certificate2 CreateTestCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
    }
}
