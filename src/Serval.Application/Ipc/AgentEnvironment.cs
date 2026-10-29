using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serval.Application.Authorization;
using Serval.Application.Services;
using Serval.Domain.Services;

namespace Serval.Application.Ipc;

public sealed record AgentEnvironment(string Service, EnvironmentModelScope Scope,
    IReadOnlyList<EnvironmentSourceMetadata> Sources,
    IReadOnlyList<EnvironmentVariableMetadata> Variables, bool CanReveal);

/// <summary>Owns exactly one authorized value. Only explicit transport writers disclose it.</summary>
[DebuggerDisplay("[REDACTED]")]
[JsonConverter(typeof(RevealedEnvironmentVariableConverter))]
public sealed class RevealedEnvironmentVariable : IDisposable
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly char[] value;
    private bool disposed;

    internal RevealedEnvironmentVariable(string service, string name, EnvironmentSourceMetadata source,
        ReadOnlySpan<char> value)
    {
        _ = new SystemServiceId(service);
        if (!ServiceOperations.IsValidVariableName(name) || source.IsMissing)
            throw new AgentProtocolException();
        Service = service;
        Name = name;
        Source = source;
        this.value = value.ToArray();
    }

    public string Service { get; }
    public string Name { get; }
    public EnvironmentModelScope Scope { get; } = EnvironmentModelScope.SupportedDeclarations;
    public EnvironmentSourceMetadata Source { get; }

    internal void Write(Utf8JsonWriter writer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.WriteStartObject();
        writer.WriteString("service", Service);
        writer.WriteString("name", Name);
        writer.WriteString("scope", Scope.ToString());
        writer.WritePropertyName("source");
        JsonSerializer.Serialize(writer, Source);
        writer.WriteString("value", value.AsSpan());
        writer.WriteEndObject();
    }

    /// <summary>Explicit HTTP disclosure writer; caller must dispose this payload after writing.</summary>
    public async Task WriteHttpAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[AgentProtocol.MaximumResultBytes];
        using var buffer = new MemoryStream(bytes, writable: true);
        buffer.SetLength(0);
        try
        {
            using (var writer = new Utf8JsonWriter(buffer))
                Write(writer);
            if (buffer.Length > AgentProtocol.MaximumResultBytes)
                throw new AgentProtocolException();
            buffer.Position = 0;
            await buffer.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
        disposed = true;
    }

    public override string ToString() => "[REDACTED]";
}

internal sealed class RevealedEnvironmentVariableConverter : JsonConverter<RevealedEnvironmentVariable>
{
    public override RevealedEnvironmentVariable Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => throw new JsonException("Explicit disclosure codec required.");

    public override void Write(Utf8JsonWriter writer, RevealedEnvironmentVariable value,
        JsonSerializerOptions options) => writer.WriteStringValue("[REDACTED]");
}
