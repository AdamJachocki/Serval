using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.HttpsPolicy;

namespace Serval.Web.Listener;

/// <summary>The server certificate with its private key and the intermediate certificates sent with it.</summary>
internal sealed record LoadedCertificate(X509Certificate2 Leaf, X509Certificate2Collection Intermediates);

/// <summary>Production listener: configuration from the root-owned file only, TLS for anything off-host.</summary>
internal static class WebListener
{
    private const int MaximumPemBytes = 16 * 1024;
    private static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);

    /// <summary>Reads <c>/etc/serval/web.json</c>; an absent file means loopback HTTP on the default port.</summary>
    public static WebListenerSettings LoadProduction() =>
        Load(WebListenerSettings.ConfigurationPath, expectedOwnerUid: 0, ProtectedFile.CurrentGroupId());

    internal static WebListenerSettings Load(string path, uint expectedOwnerUid, uint expectedGroupId)
    {
        var content = ProtectedFile.ReadIfPresent(
            path, ProtectedFileKind.Configuration, WebListenerSettings.MaximumConfigurationBytes,
            expectedOwnerUid, expectedGroupId);
        return content is null ? WebListenerSettings.Default : WebListenerSettings.Parse(content);
    }

    /// <summary>Loads the leaf certificate and key; failures never include file content or key material.</summary>
    internal static X509Certificate2 LoadCertificate(WebTlsSettings tls, uint expectedOwnerUid, uint expectedGroupId)
    {
        var loaded = LoadCertificateChain(tls, expectedOwnerUid, expectedGroupId);
        foreach (var intermediate in loaded.Intermediates)
        {
            intermediate.Dispose();
        }

        return loaded.Leaf;
    }

    /// <summary>
    /// Loads the certificate file as a chain: the first certificate is the server certificate (it must match the
    /// key), every following one is an intermediate that is sent to clients so they can build the chain offline.
    /// </summary>
    internal static LoadedCertificate LoadCertificateChain(WebTlsSettings tls, uint expectedOwnerUid, uint expectedGroupId)
    {
        var certificate = ProtectedFile.ReadIfPresent(
            tls.CertificatePath, ProtectedFileKind.Certificate, MaximumPemBytes, expectedOwnerUid, expectedGroupId);
        var key = ProtectedFile.ReadIfPresent(
            tls.KeyPath, ProtectedFileKind.PrivateKey, MaximumPemBytes, expectedOwnerUid, expectedGroupId);
        if (certificate is null || key is null)
        {
            throw new WebListenerException("The TLS certificate or key is missing.");
        }

        try
        {
            var certificatePem = Encoding.ASCII.GetString(certificate);
            if (certificatePem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                // The certificate file may be world-readable; a key in it was published by mistake.
                throw new WebListenerException("The TLS certificate file must not contain a private key.");
            }

            var loaded = X509Certificate2.CreateFromPem(certificatePem, Encoding.ASCII.GetString(key));
            if (!loaded.HasPrivateKey)
            {
                loaded.Dispose();
                throw new WebListenerException("The TLS certificate and key do not match.");
            }

            var all = new X509Certificate2Collection();
            all.ImportFromPem(certificatePem);
            var intermediates = new X509Certificate2Collection();
            for (var index = 0; index < all.Count; index++)
            {
                if (index == 0)
                {
                    all[index].Dispose();
                }
                else
                {
                    intermediates.Add(all[index]);
                }
            }

            return new LoadedCertificate(loaded, intermediates);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or DecoderFallbackException)
        {
            throw new WebListenerException("The TLS certificate or key is invalid or does not match.");
        }
    }

    /// <summary>
    /// Replaces every other source of listener settings (<c>ASPNETCORE_URLS</c>, <c>--urls</c>, the
    /// <c>Kestrel</c> configuration section) with one explicit endpoint.
    /// </summary>
    public static void ConfigureKestrel(WebApplicationBuilder builder, WebListenerSettings settings) =>
        ConfigureKestrel(builder, settings, tls => LoadCertificateChain(tls, 0, ProtectedFile.CurrentGroupId()));

    internal static void ConfigureKestrel(
        WebApplicationBuilder builder, WebListenerSettings settings, Func<WebTlsSettings, LoadedCertificate> loadCertificate)
    {
        settings.ThrowIfUnsafe();
        var certificate = settings.Tls is null ? null : loadCertificate(settings.Tls);
        // With PreferHostingUrls the server would bind the hosting URLs instead of the explicit endpoint.
        builder.WebHost.UseSetting(WebHostDefaults.PreferHostingUrlsKey, "false");
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Discards the loader bound to the "Kestrel" configuration section by the web defaults.
            options.Configure();
            options.Listen(settings.Address, settings.Port, listen =>
            {
                if (certificate is not null)
                {
                    listen.UseHttps(https =>
                    {
                        https.ServerCertificate = certificate.Leaf;
                        if (certificate.Intermediates.Count > 0)
                        {
                            https.ServerCertificateChain = certificate.Intermediates;
                        }
                    });
                }
            });
        });
        if (settings.Tls is not null)
        {
            builder.Services.AddHsts(options => options.MaxAge = HstsMaxAge);
            builder.Services.AddHttpsRedirection(options => options.HttpsPort = settings.Port);
        }
    }

    /// <summary>
    /// HSTS and HTTPS redirection exist only where HTTPS exists: always outside Production (the host's own
    /// configuration, unchanged), and in Production only when TLS is configured. Plain loopback HTTP has neither.
    /// </summary>
    public static void UseTransportSecurity(IApplicationBuilder app, WebListenerSettings? settings, bool isDevelopment)
    {
        if (settings is not null && settings.Tls is null)
        {
            return;
        }

        if (settings is not null || !isDevelopment)
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
    }
}
