using System.Security.Cryptography;

namespace Serval.Agent;

public interface IAgentAccountValidity
{
    Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken);
}

public sealed record AgentPrincipal(string Name, uint Uid);

/// <summary>Agent-owned, restart-volatile opaque sessions. Only token digests are retained.</summary>
public sealed class AgentSessions(
    TimeSpan idleTimeout, IAgentAccountValidity accountValidity, TimeProvider timeProvider)
{
    private const int MaximumSessions = 4096;
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);

    public string Create(string accountName, uint uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        if (idleTimeout <= TimeSpan.Zero || idleTimeout > TimeSpan.FromHours(2))
        {
            throw new InvalidOperationException("Agent session configuration is invalid.");
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var token = Encode(bytes);
            var digest = Digest(bytes);
            lock (gate)
            {
                RemoveExpired(timeProvider.GetTimestamp());
                if (sessions.Count >= MaximumSessions)
                {
                    throw new InvalidOperationException("Agent session capacity is exhausted.");
                }

                sessions.Add(digest, new Session(
                    accountName, uid, timeProvider.GetUtcNow(), timeProvider.GetTimestamp()));
            }

            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task<AgentPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryDigest(token, out var digest))
        {
            return null;
        }

        Session snapshot;
        lock (gate)
        {
            if (!sessions.TryGetValue(digest, out snapshot!) || IsExpired(snapshot, timeProvider.GetTimestamp()))
            {
                sessions.Remove(digest);
                return null;
            }
        }

        bool isCurrent;
        try
        {
            isCurrent = await accountValidity.IsCurrentAsync(
                snapshot.Name, snapshot.Uid, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            isCurrent = false;
        }

        lock (gate)
        {
            if (!sessions.TryGetValue(digest, out var current) ||
                !ReferenceEquals(current, snapshot) ||
                IsExpired(current, timeProvider.GetTimestamp()) || !isCurrent)
            {
                sessions.Remove(digest);
                return null;
            }

            return new AgentPrincipal(current.Name, current.Uid);
        }
    }

    public void RecordAcceptedActivity(string token)
    {
        if (!TryDigest(token, out var digest))
        {
            return;
        }

        lock (gate)
        {
            var now = timeProvider.GetTimestamp();
            if (sessions.TryGetValue(digest, out var session) && !IsExpired(session, now))
            {
                session.LastActivityTimestamp = now;
            }
        }
    }

    public bool Revoke(string token)
    {
        if (!TryDigest(token, out var digest))
        {
            return false;
        }

        lock (gate)
        {
            return sessions.Remove(digest);
        }
    }

    private void RemoveExpired(long now)
    {
        foreach (var item in sessions.Where(item => IsExpired(item.Value, now)).ToArray())
        {
            sessions.Remove(item.Key);
        }
    }

    private bool IsExpired(Session session, long now) =>
        timeProvider.GetElapsedTime(session.LastActivityTimestamp, now) >= idleTimeout;

    private static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Digest(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static bool TryDigest(string token, out string digest)
    {
        digest = string.Empty;
        if (token is null || token.Length != 43 ||
            token.Any(value => value is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");
            try
            {
                if (bytes.Length != 32 || !string.Equals(Encode(bytes), token, StringComparison.Ordinal))
                {
                    return false;
                }

                digest = Digest(bytes);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class Session(string name, uint uid, DateTimeOffset created, long timestamp)
    {
        internal string Name { get; } = name;

        internal uint Uid { get; } = uid;

        internal DateTimeOffset Created { get; } = created;

        internal long LastActivityTimestamp { get; set; } = timestamp;
    }
}
