using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serval.Application.Ipc;
using Serval.Application.Services;
using Serval.Web;
using Xunit;

namespace Serval.Web.Tests;

public sealed partial class EnvironmentPagesTests
{
    public static bool BrowserEnabled => Environment.GetEnvironmentVariable("SERVAL_BROWSER_TESTS") == "1";

    [Fact(Skip = "Requires configured Playwright browser runtime.", SkipUnless = nameof(BrowserEnabled))]
    public async Task BrowserRevealHideAndNavigation()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var certificateRequest = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost", key, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var generatedCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificateBytes = generatedCertificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx);
        using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(certificateBytes, null);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(certificateBytes);
        var agent = new EnvironmentClient { PermitReveal = true };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseStaticWebAssets();
            builder.ConfigureTestServices(services => services.AddSingleton<IAgentIpcClient>(agent));
        });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        using var client = factory.CreateClient();
        var root = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var script = Path.GetFullPath(Path.Combine(root, "../../tests/Serval.Web.Tests/browser/environment.cjs"));
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("node")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add(script);
        var server = factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addresses = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        process.StartInfo.ArgumentList.Add(addresses!.Addresses.Single());
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
            _ = await stdout;
            var diagnostic = await stderr;
            var stage = Regex.Match(diagnostic, "stage=([a-z-]+)").Groups[1].Value;
            var state = Regex.Match(diagnostic, "state=([^\r\n]+)").Groups[1].Value;
            var network = Regex.Match(diagnostic, "network=(net::[A-Z_]+|unknown)").Groups[1].Value;
            Assert.True(process.ExitCode == 0, "Browser environment flow failed at stage: " + stage + " " + state + " " + network + " " + diagnostic.Trim());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task MetadataIsIndependentAndRevealRequiresCsrf()
    {
        var agent = new EnvironmentClient();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddSingleton<IAgentIpcClient>(agent));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", "__Host-ServalSession=opaque-token");
        using var page = await client.GetAsync("/Services/Details?service=worker.service", TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(page.IsSuccessStatusCode);
        Assert.Equal(0, agent.EnvironmentCalls);
        Assert.Contains("service-environment", html, StringComparison.Ordinal);
        using var metadata = await client.GetAsync("/Services/Details?handler=Environment&service=worker.service", TestContext.Current.CancellationToken);
        Assert.True(metadata.IsSuccessStatusCode);
        Assert.True(metadata.Headers.CacheControl?.NoStore);
        var json = await metadata.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("CONFIG", json, StringComparison.Ordinal);
        Assert.DoesNotContain("value", json, StringComparison.OrdinalIgnoreCase);
        foreach (var token in new[] { "", "invalid" })
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["service"] = "worker.service",
                ["name"] = "CONFIG",
                ["__RequestVerificationToken"] = token,
            });
            using var rejected = await client.PostAsync("/Services/Details?handler=Reveal", body, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        Assert.Equal(0, agent.RevealCalls);
        var match = TokenPattern().Match(html);
        Assert.True(match.Success);
        using var acceptedBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["service"] = "worker.service",
            ["name"] = "CONFIG",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        });
        using var denied = await client.PostAsync("/Services/Details?handler=Reveal", acceptedBody, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.True(denied.Headers.CacheControl?.NoStore);
        Assert.Equal(1, agent.RevealCalls);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private sealed class EnvironmentClient : IAgentIpcClient
    {
        public int EnvironmentCalls { get; private set; }
        public int RevealCalls { get; private set; }
        public bool PermitReveal { get; init; }
        public async Task<AgentResult> SendAsync(AgentRequest request, CancellationToken cancellationToken)
        {
            AgentResult result;
            switch (request)
            {
                case AgentRequest.InspectService:
                    result = new AgentResult.InspectService(AgentResultCode.Success,
                        new AgentService("worker.service", "Worker", "loaded", "active", "running"));
                    break;
                case AgentRequest.ListEnvironment:
                    EnvironmentCalls++;
                    result = new AgentResult.ListEnvironment(AgentResultCode.Success,
                        new AgentEnvironment("worker.service", EnvironmentModelScope.SupportedDeclarations,
                            [new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment)],
                            [new EnvironmentVariableMetadata("CONFIG", 0), new EnvironmentVariableMetadata("SECOND", 0)], PermitReveal));
                    break;
                case AgentRequest.RevealEnvironmentVariable:
                    RevealCalls++;
                    if (PermitReveal)
                    {
                        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            code = "Success",
                            variable = new
                            {
                                service = "worker.service",
                                name = ((AgentRequest.RevealEnvironmentVariable)request).Name,
                                scope = "SupportedDeclarations",
                                source = new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment),
                                value = ((AgentRequest.RevealEnvironmentVariable)request).Name == "SECOND" ? "" : Guid.NewGuid().ToString("N") + "\n<inert>",
                            },
                        });
                        using var frame = new MemoryStream();
                        var header = new byte[4];
                        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
                        frame.Write(header);
                        frame.Write(payload);
                        frame.Position = 0;
                        try { result = await AgentProtocol.ReadResultAsync(frame, request, cancellationToken); }
                        finally
                        {
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
                            System.Security.Cryptography.CryptographicOperations.ZeroMemory(frame.GetBuffer());
                        }
                    }
                    else result = new AgentResult.RevealEnvironmentVariable(AgentResultCode.Denied, null);
                    break;
                default: throw new InvalidOperationException("Unexpected request.");
            }
            return result;
        }
    }
}
