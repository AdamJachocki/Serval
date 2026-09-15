using System.Diagnostics;

namespace Serval.Application.Services;

/// <summary>
/// Explicit access to sensitive values. Do not log, persist, serialize, or retain revealed values.
/// Managed strings cannot promise deterministic erasure; callers must minimize their lifetime.
/// </summary>
[DebuggerDisplay("[REDACTED]")]
public sealed class EnvironmentValues
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly Dictionary<string, string> values;

    public EnvironmentValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            _ = new EnvironmentVariableMetadata(pair.Key, 0);
            if (pair.Value is null || !this.values.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException("Invalid environment values.", nameof(values));
        }
    }

    /// <summary>Explicit secret access; authorization belongs to the future privileged caller.</summary>
    public string Reveal(string name) => values.TryGetValue(name, out var value)
        ? value : throw new KeyNotFoundException("Environment variable is absent.");

    internal bool HasExactly(IEnumerable<string> names) => values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names);

    public override string ToString() => "[REDACTED]";
}
