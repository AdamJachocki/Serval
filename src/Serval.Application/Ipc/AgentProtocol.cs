using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serval.Application.Authorization;
using Serval.Application.Services;

namespace Serval.Application.Ipc;

/// <summary>Strict, length-prefixed UTF-8 JSON request codec. Invalid frames never dispatch.</summary>
public static class AgentProtocol
{
    public const int Version = 1;
    public const int MaximumFrameBytes = 16 * 1024;
    public const int MaximumResultBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<AgentRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is <= 0 or > MaximumFrameBytes)
        {
            throw new AgentProtocolException();
        }

        var frame = new byte[size];
        try
        {
            await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
            return ParseRequest(frame);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    public static AgentRequest ParseRequest(ReadOnlySpan<byte> frame)
    {
        if (frame.Length is 0 or > MaximumFrameBytes)
        {
            throw new AgentProtocolException();
        }

        try
        {
            var json = StrictUtf8.GetString(frame);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AgentProtocolException();
            }

            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!fields.TryAdd(property.Name, property.Value))
                {
                    throw new AgentProtocolException();
                }
            }

            if (!fields.TryGetValue("version", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number != Version)
            {
                throw new AgentProtocolException();
            }

            var operation = GetString(fields, "operation", 32);
            var correlationId = GetString(fields, "correlationId", 64);
            if (correlationId.Length == 0 || !correlationId.All(IsCorrelationCharacter))
            {
                throw new AgentProtocolException();
            }

            return operation switch
            {
                "Login" => WithFields(fields, ["version", "operation", "correlationId", "username", "password"],
                    () => new AgentRequest.Login(correlationId,
                        GetString(fields, "username", 128), GetPassword(fields))),
                "Logout" => WithFields(fields, ["version", "operation", "correlationId", "session"],
                    () => new AgentRequest.Logout(correlationId, GetString(fields, "session", 128))),
                "ListServices" => WithFields(fields, ["version", "operation", "correlationId", "session"],
                    () => new AgentRequest.ListServices(correlationId, GetString(fields, "session", 128))),
                "InspectService" => WithFields(fields,
                    ["version", "operation", "correlationId", "session", "service"],
                    () => new AgentRequest.InspectService(correlationId,
                        GetString(fields, "session", 128), GetString(fields, "service", 255))),
                "ListEnvironment" => WithFields(fields,
                    ["version", "operation", "correlationId", "session", "service"],
                    () => new AgentRequest.ListEnvironment(correlationId,
                        GetString(fields, "session", 128), GetString(fields, "service", 255))),
                "RevealEnvironmentVariable" => WithFields(fields,
                    ["version", "operation", "correlationId", "session", "service", "name"],
                    () => new AgentRequest.RevealEnvironmentVariable(correlationId,
                        GetString(fields, "session", 128), GetString(fields, "service", 255), GetVariableName(fields))),
                "ListGrants" => WithFields(fields, ["version", "operation", "correlationId", "session"],
                    () => new AgentRequest.ListGrants(correlationId, GetString(fields, "session", 128))),
                "AddGrant" => WithFields(fields,
                    ["version", "operation", "correlationId", "session", "subjectKind", "subject", "grantOperation", "service"],
                    () => new AgentRequest.AddGrant(correlationId, GetString(fields, "session", 128),
                        GetString(fields, "subjectKind", 16), GetString(fields, "subject", 128),
                        GetString(fields, "grantOperation", 32), GetString(fields, "service", 255))),
                "RemoveGrant" => WithFields(fields,
                    ["version", "operation", "correlationId", "session", "grantId"],
                    () => new AgentRequest.RemoveGrant(correlationId,
                        GetString(fields, "session", 128), GetPositiveId(fields, "grantId"))),
                _ => throw new AgentProtocolException(),
            };
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new AgentProtocolException();
        }
    }

    public static async Task WriteRequestAsync(Stream stream, AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(request);
        var payload = JsonSerializer.SerializeToUtf8Bytes(ToWireRequest(request));
        if (payload.Length is 0 or > MaximumFrameBytes)
        {
            throw new AgentProtocolException();
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        try
        {
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static async Task WriteResultAsync(Stream stream, AgentResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(result);
        var payload = new byte[MaximumResultBytes];
        try
        {
            using var buffer = new MemoryStream(payload, writable: true);
            buffer.SetLength(0);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                if (result is AgentResult.RevealEnvironmentVariable reveal)
                {
                    writer.WriteStartObject();
                    writer.WriteString("code", reveal.Code.ToString());
                    if (reveal.Code == AgentResultCode.Success)
                    {
                        if (reveal.Variable is null) throw new AgentProtocolException();
                        writer.WritePropertyName("variable");
                        reveal.Variable.Write(writer);
                    }
                    else if (reveal.Failure is not null)
                        writer.WriteString("failure", reveal.Failure);
                    writer.WriteEndObject();
                }
                else
                    JsonSerializer.Serialize(writer, ToWireResult(result));
            }
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, checked((int)buffer.Length));
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload.AsMemory(0, checked((int)buffer.Length)), cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            throw new AgentProtocolException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static async Task WriteInvalidRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        await WriteFailureAsync(stream, AgentResultCode.InvalidRequest, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task WriteFailureAsync(
        Stream stream, AgentResultCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { code = code.ToString() });
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AgentResult> ReadResultAsync(
        Stream stream, AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(request);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is <= 0 or > MaximumResultBytes)
        {
            throw new AgentProtocolException();
        }

        var frame = new byte[size];
        try
        {
            await stream.ReadExactlyAsync(frame, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(frame);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var codeField) ||
                codeField.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<AgentResultCode>(codeField.GetString(), false, out var code) ||
                !Enum.IsDefined(code))
            {
                throw new AgentProtocolException();
            }

            if (request is AgentRequest.ListEnvironment or AgentRequest.RevealEnvironmentVariable)
                return ReadEnvironmentResult(root, request, code);

            if (code != AgentResultCode.Success)
            {
                return FailureResult(request, code);
            }

            return request switch
            {
                AgentRequest.Login => new AgentResult.Login(code,
                    root.GetProperty("session").GetString()),
                AgentRequest.Logout => new AgentResult.Logout(code),
                AgentRequest.ListServices => new AgentResult.ListServices(code,
                    JsonSerializer.Deserialize<AgentService[]>(root.GetProperty("services")) ??
                    throw new AgentProtocolException()),
                AgentRequest.InspectService => new AgentResult.InspectService(code,
                    JsonSerializer.Deserialize<AgentService>(root.GetProperty("service")) ??
                    throw new AgentProtocolException()),
                AgentRequest.ListGrants => new AgentResult.ListGrants(code,
                    JsonSerializer.Deserialize<AgentGrant[]>(root.GetProperty("grants")) ??
                    throw new AgentProtocolException()),
                AgentRequest.AddGrant => new AgentResult.AddGrant(code,
                    root.GetProperty("grantId").GetInt64()),
                AgentRequest.RemoveGrant => new AgentResult.RemoveGrant(code),
                _ => throw new AgentProtocolException(),
            };
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or
            KeyNotFoundException or InvalidOperationException)
        {
            throw new AgentProtocolException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    private static AgentResult FailureResult(AgentRequest request, AgentResultCode code) => request switch
    {
        AgentRequest.Login => new AgentResult.Login(code, null),
        AgentRequest.Logout => new AgentResult.Logout(code),
        AgentRequest.ListServices => new AgentResult.ListServices(code, null),
        AgentRequest.InspectService => new AgentResult.InspectService(code, null),
        AgentRequest.ListGrants => new AgentResult.ListGrants(code, null),
        AgentRequest.AddGrant => new AgentResult.AddGrant(code, null),
        AgentRequest.RemoveGrant => new AgentResult.RemoveGrant(code),
        AgentRequest.ListEnvironment => new AgentResult.ListEnvironment(code, null),
        AgentRequest.RevealEnvironmentVariable => new AgentResult.RevealEnvironmentVariable(code, null),
        _ => throw new AgentProtocolException(),
    };

    private static object ToWireRequest(AgentRequest request) => request switch
    {
        AgentRequest.Login item => new { version = Version, operation = "Login", correlationId = item.CorrelationId, username = item.Username, password = item.Password },
        AgentRequest.Logout item => new { version = Version, operation = "Logout", correlationId = item.CorrelationId, session = item.Session },
        AgentRequest.ListServices item => new { version = Version, operation = "ListServices", correlationId = item.CorrelationId, session = item.Session },
        AgentRequest.InspectService item => new { version = Version, operation = "InspectService", correlationId = item.CorrelationId, session = item.Session, service = item.Service },
        AgentRequest.ListGrants item => new { version = Version, operation = "ListGrants", correlationId = item.CorrelationId, session = item.Session },
        AgentRequest.AddGrant item => new { version = Version, operation = "AddGrant", correlationId = item.CorrelationId, session = item.Session, subjectKind = item.SubjectKind, subject = item.Subject, grantOperation = item.Operation, service = item.Service },
        AgentRequest.RemoveGrant item => new { version = Version, operation = "RemoveGrant", correlationId = item.CorrelationId, session = item.Session, grantId = item.GrantId },
        AgentRequest.ListEnvironment item => new { version = Version, operation = "ListEnvironment", correlationId = item.CorrelationId, session = item.Session, service = item.Service },
        AgentRequest.RevealEnvironmentVariable item => new { version = Version, operation = "RevealEnvironmentVariable", correlationId = item.CorrelationId, session = item.Session, service = item.Service, name = item.Name },
        _ => throw new AgentProtocolException(),
    };

    private static object ToWireResult(AgentResult result) => result switch
    {
        AgentResult.Login item => new { code = item.Code.ToString(), session = item.Session },
        AgentResult.Logout item => new { code = item.Code.ToString() },
        AgentResult.ListServices item => new { code = item.Code.ToString(), services = item.Services },
        AgentResult.InspectService item => new { code = item.Code.ToString(), service = item.Service },
        AgentResult.ListGrants item => new { code = item.Code.ToString(), grants = item.Grants },
        AgentResult.AddGrant item => new { code = item.Code.ToString(), grantId = item.GrantId },
        AgentResult.RemoveGrant item => new { code = item.Code.ToString() },
        AgentResult.ListEnvironment item when item.Code == AgentResultCode.Success => new { code = item.Code.ToString(), environment = item.Environment },
        AgentResult.ListEnvironment item => new { code = item.Code.ToString(), failure = item.Failure },
        _ => throw new AgentProtocolException(),
    };

    private static T WithFields<T>(Dictionary<string, JsonElement> fields, string[] expected, Func<T> factory)
    {
        if (fields.Count != expected.Length || fields.Keys.Any(key => !expected.Contains(key, StringComparer.Ordinal)))
        {
            throw new AgentProtocolException();
        }

        return factory();
    }

    private static string GetString(Dictionary<string, JsonElement> fields, string key, int maximum)
    {
        if (!fields.TryGetValue(key, out var field) || field.ValueKind != JsonValueKind.String)
        {
            throw new AgentProtocolException();
        }

        var value = field.GetString()!;
        if (value.Length is 0 || value.Length > maximum || value.Any(char.IsControl))
        {
            throw new AgentProtocolException();
        }

        return value;
    }

    private static long GetPositiveId(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out var field) || field.ValueKind != JsonValueKind.Number ||
            !field.TryGetInt64(out var value) || value <= 0)
        {
            throw new AgentProtocolException();
        }

        return value;
    }

    private static string GetPassword(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("password", out var field) || field.ValueKind != JsonValueKind.String)
        {
            throw new AgentProtocolException();
        }

        var value = field.GetString()!;
        if (value.Length is 0 or > 1024 || value.Contains('\0'))
        {
            throw new AgentProtocolException();
        }

        return value;
    }

    private static bool IsCorrelationCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';

    private static string GetVariableName(Dictionary<string, JsonElement> fields)
    {
        var name = GetString(fields, "name", 4096);
        return ServiceOperations.IsValidVariableName(name) ? name : throw new AgentProtocolException();
    }

    private static void RequireFields(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new AgentProtocolException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new AgentProtocolException();
        if (names.Count != allowed.Length) throw new AgentProtocolException();
    }

    private static AgentResult ReadEnvironmentResult(JsonElement root, AgentRequest request, AgentResultCode code)
    {
        if (root.GetProperty("code").GetString() != code.ToString()) throw new AgentProtocolException();
        if (code != AgentResultCode.Success)
        {
            if (root.TryGetProperty("failure", out _)) RequireFields(root, "code", "failure");
            else RequireFields(root, "code");
            string? failure = root.TryGetProperty("failure", out var field) && field.ValueKind != JsonValueKind.Null
                ? field.GetString() : null;
            if (failure is not null && (!Enum.TryParse<EnvironmentReadFailureCode>(failure, out var parsed) ||
                !Enum.IsDefined(parsed) || parsed.ToString() != failure)) throw new AgentProtocolException();
            return request is AgentRequest.ListEnvironment
                ? new AgentResult.ListEnvironment(code, null, failure)
                : new AgentResult.RevealEnvironmentVariable(code, null, failure);
        }
        try
        {
            if (request is AgentRequest.ListEnvironment)
            {
                RequireFields(root, "code", "environment");
                var data = root.GetProperty("environment");
                RequireFields(data, "Service", "Scope", "Sources", "Variables", "CanReveal");
                foreach (var source in data.GetProperty("Sources").EnumerateArray())
                    RequireFields(source, "Id", "Kind", "IsOptional", "IsMissing");
                foreach (var variable in data.GetProperty("Variables").EnumerateArray())
                    RequireFields(variable, "Name", "WinningSourceId");
                var environment = JsonSerializer.Deserialize<AgentEnvironment>(data) ?? throw new AgentProtocolException();
                _ = new Serval.Domain.Services.SystemServiceId(environment.Service);
                if (environment.Scope != EnvironmentModelScope.SupportedDeclarations ||
                    environment.Sources is null || environment.Variables is null ||
                    environment.Sources.Any(s => s is null) || environment.Variables.Any(v => v is null) ||
                    environment.Sources.Select(s => s.Id).Distinct().Count() != environment.Sources.Count ||
                    environment.Variables.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() != environment.Variables.Count ||
                    environment.Variables.Any(v => !environment.Sources.Any(s => s.Id == v.WinningSourceId && !s.IsMissing)))
                    throw new AgentProtocolException();
                return new AgentResult.ListEnvironment(code, environment);
            }
            RequireFields(root, "code", "variable");
            var item = root.GetProperty("variable");
            RequireFields(item, "service", "name", "scope", "source", "value");
            RequireFields(item.GetProperty("source"), "Id", "Kind", "IsOptional", "IsMissing");
            if (item.GetProperty("scope").GetString() != "SupportedDeclarations" ||
                item.GetProperty("name").GetString() != ((AgentRequest.RevealEnvironmentVariable)request).Name)
                throw new AgentProtocolException();
            var value = item.GetProperty("value").GetString() ?? throw new AgentProtocolException();
            return new AgentResult.RevealEnvironmentVariable(code, new RevealedEnvironmentVariable(
                item.GetProperty("service").GetString()!, item.GetProperty("name").GetString()!,
                JsonSerializer.Deserialize<EnvironmentSourceMetadata>(item.GetProperty("source")) ?? throw new AgentProtocolException(),
                value.AsSpan()));
        }
        catch (ArgumentException)
        {
            throw new AgentProtocolException();
        }
    }
}

public sealed class AgentProtocolException : Exception
{
    public AgentProtocolException() : base("Invalid Agent protocol message.")
    {
    }
}
