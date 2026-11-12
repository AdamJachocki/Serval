using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Serval.Web.Listener;

/// <summary>Raised for any listener configuration problem; the message never contains file content.</summary>
internal sealed class WebListenerException(string message) : Exception(message);

/// <summary>Certificate and key files, both located directly in <see cref="WebListenerSettings.TlsDirectory"/>.</summary>
internal sealed record WebTlsSettings(string CertificatePath, string KeyPath);

/// <summary>Validated Production listener: one explicit endpoint, HTTP only on loopback.</summary>
internal sealed partial record WebListenerSettings(IPAddress Address, int Port, WebTlsSettings? Tls)
{
    public const string ConfigurationPath = "/etc/serval/web.json";
    public const string TlsDirectory = "/etc/serval/tls/";
    private const int MinimumPort = 1024;
    private const int MaximumPort = 65535;
    public const int MaximumConfigurationBytes = 4096;

    public static WebListenerSettings Default { get; } = new(IPAddress.Loopback, 9080, null);

    /// <summary>Throws when plain HTTP would be reachable from outside the host.</summary>
    public void ThrowIfUnsafe()
    {
        if (Tls is null && !IPAddress.IsLoopback(Address))
        {
            throw new WebListenerException("A non-loopback listener requires TLS.");
        }
    }

    public static WebListenerSettings Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 3,
            });
            using var document = JsonDocument.ParseValue(ref reader);
            if (reader.Read())
            {
                throw new WebListenerException("Listener configuration is malformed.");
            }

            var settings = FromRoot(document.RootElement);
            settings.ThrowIfUnsafe();
            return settings;
        }
        catch (JsonException)
        {
            throw new WebListenerException("Listener configuration is malformed.");
        }
    }

    private static WebListenerSettings FromRoot(JsonElement root)
    {
        var fields = Fields(root, "address", "port", "tls");
        if (!fields.TryGetValue("address", out var address) || address.ValueKind != JsonValueKind.String ||
            !fields.TryGetValue("port", out var port) || port.ValueKind != JsonValueKind.Number)
        {
            throw new WebListenerException("Listener configuration is invalid.");
        }

        var parsedAddress = ParseAddress(address.GetString()!);
        if (!port.TryGetInt32(out var number) || number is < MinimumPort or > MaximumPort)
        {
            throw new WebListenerException("Listener port is outside the allowed range.");
        }

        WebTlsSettings? tls = null;
        if (fields.TryGetValue("tls", out var tlsElement) && tlsElement.ValueKind != JsonValueKind.Null)
        {
            var tlsFields = Fields(tlsElement, "certificate", "key");
            if (tlsFields.Count != 2 ||
                tlsFields["certificate"].ValueKind != JsonValueKind.String ||
                tlsFields["key"].ValueKind != JsonValueKind.String)
            {
                throw new WebListenerException("Listener TLS configuration is invalid.");
            }

            tls = new WebTlsSettings(
                ValidateTlsPath(tlsFields["certificate"].GetString()!),
                ValidateTlsPath(tlsFields["key"].GetString()!));
        }

        return new WebListenerSettings(parsedAddress, number, tls);
    }

    private static Dictionary<string, JsonElement> Fields(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new WebListenerException("Listener configuration is invalid.");
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) ||
                !fields.TryAdd(property.Name, property.Value))
            {
                throw new WebListenerException("Listener configuration contains an unknown or duplicate field.");
            }
        }

        return fields;
    }

    private static IPAddress ParseAddress(string value)
    {
        // Literal addresses only: no host names, no zone identifiers, no short IPv4 forms.
        if (value.Length is >= 2 and <= 45 && !value.Contains('%') &&
            (value.Contains(':') ? IPv6Characters().IsMatch(value) : DottedIPv4().IsMatch(value)) &&
            IPAddress.TryParse(value, out var address) &&
            address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
        {
            return address;
        }

        throw new WebListenerException("Listener address must be a literal IPv4 or IPv6 address.");
    }

    private static string ValidateTlsPath(string path)
    {
        if (!TlsFile().IsMatch(path) || !path.StartsWith(TlsDirectory, StringComparison.Ordinal))
        {
            throw new WebListenerException("TLS files must be located directly in " + TlsDirectory + ".");
        }

        return path;
    }

    [GeneratedRegex("^[0-9A-Fa-f:.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IPv6Characters();

    [GeneratedRegex(@"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$", RegexOptions.CultureInvariant)]
    private static partial Regex DottedIPv4();

    [GeneratedRegex(@"^/etc/serval/tls/[A-Za-z0-9][A-Za-z0-9_-]*(\.[A-Za-z0-9_-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TlsFile();
}
