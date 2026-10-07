namespace Serval.Agent;

public interface IAgentIdentityChecks : IAgentAccountValidity
{
    Task<AgentAuthentication> AuthenticateAsync(
        string username, string password, CancellationToken cancellationToken);

    Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken);

    Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken);

    Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken);
}

public sealed record AgentAuthentication(AgentResultStatus Status, LinuxAccount? Account);

public enum AgentResultStatus
{
    Success,
    Denied,
    Unavailable,
}

/// <summary>Serialized, bounded-wait calls into PAM and NSS for Agent authentication.</summary>
public sealed class LinuxAccountChecks : IAgentIdentityChecks
{
    private static readonly TimeSpan NativeWait = TimeSpan.FromSeconds(5);
    private readonly BoundedNativeCalls calls;
    private readonly Func<string, string, LinuxAccount?> authenticate;
    private readonly Func<string, uint, bool> accountIsCurrent;
    private readonly Func<string, LinuxAccount?> resolveAccount;
    private readonly Func<string, LinuxGroup?> resolveGroup;

    public LinuxAccountChecks()
        : this(AuthenticateNative, CheckNative, LinuxNss.ResolveAccount,
            LinuxNss.ResolveGroup, NativeWait)
    {
    }

    internal LinuxAccountChecks(
        Func<string, string, LinuxAccount?> authenticate,
        Func<string, uint, bool> accountIsCurrent,
        TimeSpan waitLimit)
        : this(authenticate, accountIsCurrent, LinuxNss.ResolveAccount,
            LinuxNss.ResolveGroup, waitLimit)
    {
    }

    internal LinuxAccountChecks(
        Func<string, string, LinuxAccount?> authenticate,
        Func<string, uint, bool> accountIsCurrent,
        Func<string, LinuxAccount?> resolveAccount,
        Func<string, LinuxGroup?> resolveGroup,
        TimeSpan waitLimit)
    {
        this.authenticate = authenticate;
        this.accountIsCurrent = accountIsCurrent;
        this.resolveAccount = resolveAccount;
        this.resolveGroup = resolveGroup;
        calls = new BoundedNativeCalls(4, waitLimit);
    }

    public async Task<LinuxAccount?> ResolveAccountAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 128 || username.Contains('\0'))
        {
            return null;
        }

        try
        {
            var result = await calls.RunAsync(() => resolveAccount(username), cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded ? result.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<LinuxAccount?> ResolveAccountByUidAsync(uint uid, CancellationToken cancellationToken)
    {
        if (uid == 0)
        {
            return null;
        }

        try
        {
            var result = await calls.RunAsync(() => LinuxNss.ResolveAccountByUid(uid),
                cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<LinuxGroup?> ResolveGroupAsync(string groupName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupName) || groupName.Length > 128 || groupName.Contains('\0'))
        {
            return null;
        }

        try
        {
            var result = await calls.RunAsync(() => resolveGroup(groupName), cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded ? result.Value : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AgentAuthentication> AuthenticateAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 128 ||
            username.Contains('\0') || password is null || password.Length > 1024 ||
            password.Contains('\0'))
        {
            return new AgentAuthentication(AgentResultStatus.Denied, null);
        }

        try
        {
            var result = await calls.RunAsync(
                () => authenticate(username, password), cancellationToken).ConfigureAwait(false);
            return !result.Succeeded
                ? new AgentAuthentication(AgentResultStatus.Unavailable, null)
                : result.Value is null
                    ? new AgentAuthentication(AgentResultStatus.Denied, null)
                    : new AgentAuthentication(AgentResultStatus.Success, result.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AgentAuthentication(AgentResultStatus.Unavailable, null);
        }
    }

    public async Task<bool> IsCurrentAsync(string name, uint uid, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Contains('\0'))
        {
            return false;
        }

        try
        {
            var result = await calls.RunAsync(
                () => accountIsCurrent(name, uid), cancellationToken).ConfigureAwait(false);
            return result.Succeeded && result.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static LinuxAccount? AuthenticateNative(string username, string password) =>
        LinuxPam.Authenticate(username, password) ? LinuxNss.ResolveAccount(username) : null;

    private static bool CheckNative(string name, uint uid) =>
        LinuxNss.ResolveAccount(name)?.Uid == uid && LinuxPam.CheckAccount(name);
}
