using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serval.Web.Listener;
using Xunit;

namespace Serval.Web.Tests;

public sealed class WebListenerSettingsTests
{
    [Fact]
    public void AbsentConfigurationMeansLoopbackHttpOnPort9080()
    {
        var settings = WebListenerSettings.Default;
        Assert.Equal(IPAddress.Loopback, settings.Address);
        Assert.Equal(9080, settings.Port);
        Assert.Null(settings.Tls);
    }

    [Fact]
    public void ValidLoopbackHttpConfigurationIsAccepted()
    {
        var settings = Parse("""{"address":"127.0.0.1","port":9081,"tls":null}""");
        Assert.Equal(IPAddress.Loopback, settings.Address);
        Assert.Equal(9081, settings.Port);
        Assert.Null(settings.Tls);
        Assert.Equal(IPAddress.IPv6Loopback, Parse("""{"address":"::1","port":1024}""").Address);
    }

    [Fact]
    public void ValidHttpsConfigurationIsAccepted()
    {
        var settings = Parse("""
            {"address":"0.0.0.0","port":9443,"tls":{"certificate":"/etc/serval/tls/cert.pem","key":"/etc/serval/tls/key.pem"}}
            """);
        Assert.Equal(IPAddress.Any, settings.Address);
        Assert.Equal(9443, settings.Port);
        Assert.Equal("/etc/serval/tls/cert.pem", settings.Tls?.CertificatePath);
        Assert.Equal("/etc/serval/tls/key.pem", settings.Tls?.KeyPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"address":"127.0.0.1","port":9080} trailing""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,}""")]
    [InlineData("""{"address":"127.0.0.1",/*x*/"port":9080}""")]
    [InlineData("""{"address":"127.0.0.1"}""")]
    [InlineData("""{"port":9080}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"urls":"http://0.0.0.0:80"}""")]
    [InlineData("""{"address":"127.0.0.1","address":"0.0.0.0","port":9080}""")]
    [InlineData("""{"address":"127.0.0.1","port":"9080"}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080.5}""")]
    [InlineData("""{"address":"localhost","port":9080}""")]
    [InlineData("""{"address":"example.com","port":9080}""")]
    [InlineData("""{"address":"127.1","port":9080}""")]
    [InlineData("""{"address":"2130706433","port":9080}""")]
    [InlineData("""{"address":"127.0.0.1 ","port":9080}""")]
    [InlineData("""{"address":"256.0.0.1","port":9080}""")]
    [InlineData("""{"address":"fe80::1%eth0","port":9080}""")]
    [InlineData("""{"address":"*","port":9080}""")]
    [InlineData("""{"address":"127.0.0.1","port":1023}""")]
    [InlineData("""{"address":"127.0.0.1","port":80}""")]
    [InlineData("""{"address":"127.0.0.1","port":65536}""")]
    [InlineData("""{"address":"127.0.0.1","port":-1}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":"/etc/serval/tls/cert.pem"}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/cert.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/cert.pem","key":"/etc/serval/tls/key.pem","extra":1}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/cert.pem","key":"/etc/serval/tls/cert.pem","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/ssl/cert.pem","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/cert.pem","key":"/tmp/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/../cert.pem","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/sub/cert.pem","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"/etc/serval/tls/.hidden","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"127.0.0.1","port":9080,"tls":{"certificate":"etc/serval/tls/cert.pem","key":"/etc/serval/tls/key.pem"}}""")]
    [InlineData("""{"address":"0.0.0.0","port":9080}""")]
    [InlineData("""{"address":"0.0.0.0","port":9080,"tls":null}""")]
    [InlineData("""{"address":"::","port":9080}""")]
    [InlineData("""{"address":"192.168.1.10","port":9080}""")]
    public void InvalidConfigurationIsRejectedWithSanitizedMessage(string json)
    {
        var exception = Assert.Throws<WebListenerException>(() => Parse(json));
        Assert.DoesNotContain("PRIVATE KEY", exception.Message, StringComparison.Ordinal);
        Assert.False(json.Length > 0 && exception.Message.Contains(json, StringComparison.Ordinal));
    }

    [Fact]
    public void NonLoopbackWithoutTlsIsRefusedWhenSettingsAreBuiltDirectly()
    {
        Assert.Throws<WebListenerException>(() => new WebListenerSettings(IPAddress.Any, 9080, null).ThrowIfUnsafe());
        new WebListenerSettings(IPAddress.Any, 9080, new WebTlsSettings("/etc/serval/tls/cert.pem", "/etc/serval/tls/key.pem")).ThrowIfUnsafe();
    }

    private static WebListenerSettings Parse(string json) => WebListenerSettings.Parse(Encoding.UTF8.GetBytes(json));
}

public sealed class ProtectedFilePolicyTests
{
    private const uint WebGroup = 998;

    [Theory]
    [InlineData(0u, true)]
    [InlineData(WebGroup, true)]
    [InlineData(5u, false)]
    public void CertificateMayBeOwnedByRootOrWebGroup(uint group, bool expected) =>
        Assert.Equal(expected, ProtectedFile.IsAcceptable(
            new ProtectedFileFacts(true, 0, group, 0x1a4), ProtectedFileKind.Certificate, WebGroup));

    [Theory]
    [InlineData(true, 0u, WebGroup, 0x1a0, false, true)] // 0640
    [InlineData(true, 0u, WebGroup, 0x1a4, false, true)] // 0644
    [InlineData(true, 0u, 0u, 0x180, false, false)] // group root is not serval-web
    [InlineData(true, 0u, WebGroup, 0x1b0, false, false)] // group write
    [InlineData(true, 0u, WebGroup, 0x1a2, false, false)] // other write
    [InlineData(true, 998u, WebGroup, 0x1a0, false, false)] // owned by the web account
    [InlineData(true, 1000u, WebGroup, 0x1a0, false, false)]
    [InlineData(false, 0u, WebGroup, 0x1a0, false, false)] // not a regular file
    [InlineData(true, 0u, WebGroup, 0x1a0, true, true)]
    [InlineData(true, 0u, 0u, 0x180, true, true)] // 0600 root:root
    [InlineData(true, 0u, WebGroup, 0x1a4, true, false)] // world readable key
    [InlineData(true, 0u, WebGroup, 0x1a1, true, false)]
    [InlineData(true, 0u, 5u, 0x1a0, true, false)] // unrelated group
    public void PolicyMatchesTheDocumentedOwnershipAndModes(
        bool regular, uint owner, uint group, int mode, bool privateKey, bool expected)
    {
        var facts = new ProtectedFileFacts(regular, owner, group, (ushort)mode);
        var kind = privateKey ? ProtectedFileKind.PrivateKey : ProtectedFileKind.Configuration;
        Assert.Equal(expected, ProtectedFile.IsAcceptable(facts, kind, WebGroup));
    }
}

[SupportedOSPlatform("linux")]
public sealed class ProtectedFileFilesystemTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "serval-web-listener-" + Guid.NewGuid().ToString("N"));

    public ProtectedFileFilesystemTests()
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AbsentFileIsReportedAsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        Assert.Null(Read(Path.Combine(directory, "missing.json"), ProtectedFileKind.Configuration));
    }

    [Fact]
    public void SafeFileIsReadFromTheOpenedDescriptor()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var path = Write("web.json", "{}", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Equal("{}", Encoding.UTF8.GetString(Read(path, ProtectedFileKind.Configuration)!));
    }

    [Fact]
    public void SymbolicLinkIsRefusedEvenWhenItsTargetIsSafe()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var target = Write("target.json", "{}", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var link = Path.Combine(directory, "web.json");
        File.CreateSymbolicLink(link, target);
        Assert.Throws<WebListenerException>(() => Read(link, ProtectedFileKind.Configuration));
    }

    [Fact]
    public void SymbolicLinkedDirectoryComponentIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var real = Directory.CreateDirectory(Path.Combine(directory, "real"));
        File.WriteAllText(Path.Combine(real.FullName, "web.json"), "{}");
        File.SetUnixFileMode(Path.Combine(real.FullName, "web.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var link = Path.Combine(directory, "link");
        Directory.CreateSymbolicLink(link, real.FullName);
        Assert.Throws<WebListenerException>(() => Read(Path.Combine(link, "web.json"), ProtectedFileKind.Configuration));
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite)]
    public void WritableByGroupOrOthersIsRefused(UnixFileMode mode)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var path = Write("web.json", "{}", mode);
        Assert.Throws<WebListenerException>(() => Read(path, ProtectedFileKind.Configuration));
    }

    [Fact]
    public void WrongOwnerOrGroupIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var path = Write("web.json", "{}", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<WebListenerException>(() =>
            ProtectedFile.ReadIfPresent(path, ProtectedFileKind.Configuration, 4096, expectedOwnerUid: ProtectedFile.CurrentUserId() + 1, ProtectedFile.CurrentGroupId()));
        Assert.Throws<WebListenerException>(() =>
            ProtectedFile.ReadIfPresent(path, ProtectedFileKind.Configuration, 4096, ProtectedFile.CurrentUserId(), ProtectedFile.CurrentGroupId() + 1));
    }

    [Fact]
    public void DirectoryEmptyAndOversizedFilesAreRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var folder = Directory.CreateDirectory(Path.Combine(directory, "folder")).FullName;
        Assert.Throws<WebListenerException>(() => Read(folder, ProtectedFileKind.Configuration));
        Assert.Throws<WebListenerException>(() => Read(Write("empty.json", string.Empty, UnixFileMode.UserRead), ProtectedFileKind.Configuration));
        Assert.Throws<WebListenerException>(() =>
            Read(Write("big.json", new string('a', 5000), UnixFileMode.UserRead), ProtectedFileKind.Configuration));
    }

    internal static byte[]? Read(string path, ProtectedFileKind kind) =>
        ProtectedFile.ReadIfPresent(path, kind, 4096, ProtectedFile.CurrentUserId(), ProtectedFile.CurrentGroupId());

    private string Write(string name, string content, UnixFileMode mode)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, mode);
        return path;
    }
}

[SupportedOSPlatform("linux")]
public sealed class WebListenerCertificateTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "serval-web-tls-" + Guid.NewGuid().ToString("N"));

    public WebListenerCertificateTests()
    {
        if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(directory);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MatchingCertificateAndKeyAreLoaded()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out var key, out _);
        using var certificate = LoadCertificate(tls);
        Assert.True(certificate.HasPrivateKey);
        key.Dispose();
    }

    [Fact]
    public void ChainFileYieldsTheLeafAndItsIntermediates()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        using var chain = TestChain.Create();
        var certificatePath = Path.Combine(directory, "cert.pem");
        var keyPath = Path.Combine(directory, "key.pem");
        File.WriteAllText(certificatePath, chain.Leaf.ExportCertificatePem() + "\n" + chain.Intermediate.ExportCertificatePem());
        File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        File.WriteAllText(keyPath, chain.LeafKey.ExportPkcs8PrivateKeyPem());
        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        var loaded = WebListener.LoadCertificateChain(
            new WebTlsSettings(certificatePath, keyPath), ProtectedFile.CurrentUserId(), ProtectedFile.CurrentGroupId());
        using var leaf = loaded.Leaf;
        Assert.Equal(chain.Leaf.Thumbprint, leaf.Thumbprint);
        Assert.True(leaf.HasPrivateKey);
        var intermediate = Assert.Single(loaded.Intermediates);
        Assert.Equal(chain.Intermediate.Thumbprint, intermediate.Thumbprint);
        intermediate.Dispose();
    }

    [Fact]
    public void CertificateFileThatContainsAPrivateKeyIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out var key, out var certificate);
        var keyPem = key.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(tls.CertificatePath, certificate.ExportCertificatePem() + "\n" + keyPem);
        File.SetUnixFileMode(tls.CertificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        var exception = Assert.Throws<WebListenerException>(() => LoadCertificate(tls));
        AssertSanitized(exception, keyPem);
        key.Dispose();
    }

    [Fact]
    public void MismatchedKeyIsRefusedWithoutKeyMaterialInTheError()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out _, out _);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherPem = other.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(tls.KeyPath, otherPem);
        File.SetUnixFileMode(tls.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var exception = Assert.Throws<WebListenerException>(() => LoadCertificate(tls));
        AssertSanitized(exception, otherPem);
    }

    [Fact]
    public void MissingCertificateOrKeyIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out _, out _);
        File.Delete(tls.KeyPath);
        Assert.Throws<WebListenerException>(() => LoadCertificate(tls));
        tls = Install(out _, out _);
        File.Delete(tls.CertificatePath);
        Assert.Throws<WebListenerException>(() => LoadCertificate(tls));
    }

    [Fact]
    public void EncryptedAndGarbageKeysAreRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out var key, out _);
        var encrypted = key.ExportEncryptedPkcs8PrivateKeyPem(
            "synthetic-passphrase", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000));
        File.WriteAllText(tls.KeyPath, encrypted);
        File.SetUnixFileMode(tls.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        AssertSanitized(Assert.Throws<WebListenerException>(() => LoadCertificate(tls)), encrypted);

        File.WriteAllText(tls.KeyPath, "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n");
        AssertSanitized(Assert.Throws<WebListenerException>(() => LoadCertificate(tls)), "AAAA");
    }

    [Fact]
    public void WorldReadableOrSymbolicLinkedKeyIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux.");
        var tls = Install(out _, out _);
        File.SetUnixFileMode(tls.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        Assert.Throws<WebListenerException>(() => LoadCertificate(tls));

        File.SetUnixFileMode(tls.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var moved = tls.KeyPath + ".real";
        File.Move(tls.KeyPath, moved);
        File.CreateSymbolicLink(tls.KeyPath, moved);
        Assert.Throws<WebListenerException>(() => LoadCertificate(tls));
    }

    private static X509Certificate2 LoadCertificate(WebTlsSettings tls) =>
        WebListener.LoadCertificate(tls, ProtectedFile.CurrentUserId(), ProtectedFile.CurrentGroupId());

    private static void AssertSanitized(WebListenerException exception, string secret)
    {
        Assert.DoesNotContain("PRIVATE KEY", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Trim(), exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    private WebTlsSettings Install(out ECDsa key, out X509Certificate2 certificate)
    {
        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=serval.test", key, HashAlgorithmName.SHA256);
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var certificatePath = Path.Combine(directory, "cert.pem");
        var keyPath = Path.Combine(directory, "key.pem");
        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.SetUnixFileMode(certificatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        return new WebTlsSettings(certificatePath, keyPath);
    }
}

public sealed class WebListenerHostingTests
{
    [Fact]
    public async Task ProductionIgnoresUrlsAndKestrelSectionAndListensOnlyOnTheConfiguredEndpoint()
    {
        var rogue = FreePort();
        var chosen = FreePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            Args = ["--urls", $"http://0.0.0.0:{rogue}"],
        });
        builder.Configuration["Kestrel:Endpoints:Rogue:Url"] = $"http://0.0.0.0:{rogue}";
        builder.Configuration["ASPNETCORE_URLS"] = $"http://0.0.0.0:{rogue}";
        builder.Configuration["urls"] = $"http://0.0.0.0:{rogue}";
        builder.Configuration["preferHostingUrls"] = "true";
        builder.Logging.ClearProviders();
        WebListener.ConfigureKestrel(builder, new WebListenerSettings(IPAddress.Loopback, chosen, null), _ => throw new InvalidOperationException());
        var app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Assert.Equal([$"http://127.0.0.1:{chosen}"], addresses.ToArray());

            using var client = new HttpClient();
            Assert.Equal("ok", await client.GetStringAsync($"http://127.0.0.1:{chosen}/", TestContext.Current.CancellationToken));
            using var probe = new TcpClient();
            await Assert.ThrowsAnyAsync<SocketException>(async () =>
                await probe.ConnectAsync(IPAddress.Loopback, rogue, TestContext.Current.CancellationToken));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServerSendsTheConfiguredIntermediateCertificates(bool withIntermediates)
    {
        using var chain = TestChain.Create();
        var port = FreePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        var tls = new WebTlsSettings("/etc/serval/tls/cert.pem", "/etc/serval/tls/key.pem");
        var intermediates = new X509Certificate2Collection();
        if (withIntermediates)
        {
            intermediates.Add(chain.Intermediate);
        }

        WebListener.ConfigureKestrel(
            builder, new WebListenerSettings(IPAddress.Loopback, port, tls),
            _ => new LoadedCertificate(chain.LeafWithKey, intermediates));
        var app = builder.Build();
        app.MapGet("/", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var sent = new List<string>();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, peerChain, _) =>
            {
                sent.AddRange(peerChain!.ChainElements.Select(element => element.Certificate.Subject));
                return true;
            });
            await ssl.AuthenticateAsClientAsync("leaf.test");

            Assert.Contains(chain.Leaf.Subject, sent);
            if (withIntermediates)
            {
                Assert.Contains(chain.Intermediate.Subject, sent);
            }
            else if (OperatingSystem.IsLinux())
            {
                // Other platforms may complete the chain from a cache of intermediates seen earlier.
                Assert.DoesNotContain(chain.Intermediate.Subject, sent);
            }
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void NonLoopbackHttpFailsBeforeAnyListenerIsConfigured()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var exception = Assert.Throws<WebListenerException>(() =>
            WebListener.ConfigureKestrel(builder, new WebListenerSettings(IPAddress.Any, 9080, null), _ => throw new InvalidOperationException()));
        Assert.Contains("TLS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificateFailureAbortsConfigurationWithoutKeyMaterial()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var tls = new WebTlsSettings("/etc/serval/tls/cert.pem", "/etc/serval/tls/key.pem");
        Assert.Throws<WebListenerException>(() => WebListener.ConfigureKestrel(
            builder, new WebListenerSettings(IPAddress.Any, 9443, tls),
            _ => throw new WebListenerException("The TLS certificate or key is invalid or does not match.")));
    }

    [Fact]
    public async Task HstsAndRedirectionExistOnlyWithTls()
    {
        using var certificate = SelfSigned();
        var tls = new WebTlsSettings("/etc/serval/tls/cert.pem", "/etc/serval/tls/key.pem");

        using (var https = await StartTestServer(new WebListenerSettings(IPAddress.Any, 9443, tls), _ => certificate))
        {
            var secure = await https.GetAsync(new Uri("https://serval.example/"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
            Assert.Equal("max-age=31536000", Assert.Single(secure.Headers.GetValues("Strict-Transport-Security")));

            var plain = await https.GetAsync(new Uri("http://serval.example/"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.TemporaryRedirect, plain.StatusCode);
            Assert.Equal("https://serval.example:9443/", plain.Headers.Location?.ToString());
        }

        using var loopback = await StartTestServer(new WebListenerSettings(IPAddress.Loopback, 9080, null), _ => throw new InvalidOperationException());
        var response = await loopback.GetAsync(new Uri("http://localhost/"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.Null(response.Headers.Location);
    }

    private static async Task<HttpClient> StartTestServer(
        WebListenerSettings settings, Func<WebTlsSettings, X509Certificate2> certificate)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        WebListener.ConfigureKestrel(builder, settings, tls => new LoadedCertificate(certificate(tls), []));
        var app = builder.Build();
        WebListener.UseTransportSecurity(app, settings, isDevelopment: false);
        app.MapGet("/", () => "ok");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app.GetTestClient();
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=serval.example", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>A throwaway root, intermediate and leaf (with key) for chain tests.</summary>
internal sealed class TestChain : IDisposable
{
    private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private TestChain()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        var rootRequest = new CertificateRequest($"CN=Serval Test Root {suffix}", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        Root = rootRequest.CreateSelfSigned(notBefore, notAfter);

        var intermediateRequest = new CertificateRequest($"CN=Serval Test Intermediate {suffix}", intermediateKey, HashAlgorithmName.SHA256);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        Intermediate = intermediateRequest.Create(Root, notBefore, notAfter, [1]);

        LeafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafRequest = new CertificateRequest("CN=leaf.test", LeafKey, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("leaf.test");
        leafRequest.CertificateExtensions.Add(names.Build());
        using (var issuer = Intermediate.CopyWithPrivateKey(intermediateKey))
        {
            Leaf = leafRequest.Create(issuer, notBefore, notAfter, [2]);
        }
        using var withKey = Leaf.CopyWithPrivateKey(LeafKey);
        // Re-imported so the key is usable by Schannel on Windows hosts.
        LeafWithKey = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
    }

    public X509Certificate2 Root { get; }

    public X509Certificate2 Intermediate { get; }

    public X509Certificate2 Leaf { get; }

    public ECDsa LeafKey { get; }

    public X509Certificate2 LeafWithKey { get; }

    public static TestChain Create() => new();

    public void Dispose()
    {
        LeafWithKey.Dispose();
        Leaf.Dispose();
        Intermediate.Dispose();
        Root.Dispose();
        LeafKey.Dispose();
        intermediateKey.Dispose();
        rootKey.Dispose();
    }
}
