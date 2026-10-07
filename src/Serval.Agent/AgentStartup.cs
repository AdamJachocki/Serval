namespace Serval.Agent;

internal static class AgentStartup
{
    internal static async Task ValidateDeploymentIdentitiesAsync(
        AgentConfiguration configuration, IAgentIdentityChecks identities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(identities);
        if (configuration.WebUid == 0 || configuration.WebGid == 0 ||
            configuration.AdminGid == 0 || string.IsNullOrWhiteSpace(configuration.AdminGroup))
        {
            throw new InvalidOperationException("Agent deployment identities are invalid.");
        }

        var webAccount = await identities.ResolveAccountByUidAsync(configuration.WebUid,
            cancellationToken).ConfigureAwait(false);
        var adminGroup = await identities.ResolveGroupAsync(configuration.AdminGroup,
            cancellationToken).ConfigureAwait(false);
        if (webAccount is null || webAccount.Name != "serval-web" ||
            webAccount.Home != "/nonexistent" ||
            webAccount.Shell != "/usr/sbin/nologin" || !webAccount.PasswordLocked ||
            webAccount.Uid != configuration.WebUid ||
            webAccount.PrimaryGid != configuration.WebGid ||
            webAccount.Groups.Count != 1 ||
            webAccount.Groups[0].Gid != configuration.WebGid ||
            webAccount.Groups[0].Name != webAccount.Name ||
            adminGroup is null || adminGroup.Name != configuration.AdminGroup ||
            adminGroup.Gid != configuration.AdminGid)
        {
            throw new InvalidOperationException("Agent deployment identities are invalid.");
        }
    }
}
