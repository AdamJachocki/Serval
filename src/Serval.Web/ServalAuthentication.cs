using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Serval.Application.Ipc;

namespace Serval.Web;

public static class ServalAuthentication
{
    public const string Scheme = "Serval.Session";
    public const string CookieName = "__Host-ServalSession";
    public const string SessionProperty = ".serval.agent-session";

    public static ClaimsPrincipal CreatePrincipal(AgentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, identity.Name),
            new Claim(ClaimTypes.NameIdentifier, identity.Uid.ToString(CultureInfo.InvariantCulture)),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}

public sealed class ServalCookieAuthenticationEvents(IAgentIpcClient agent)
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!context.Properties.Items.TryGetValue(ServalAuthentication.SessionProperty, out var session) ||
            string.IsNullOrWhiteSpace(session))
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        var result = (AgentResult.ValidateSession)await agent.SendAsync(
            new AgentRequest.ValidateSession(Guid.NewGuid().ToString("N"), session),
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        var identity = result.Code == AgentResultCode.Success ? result.Identity : null;
        var ticketName = context.Principal?.FindFirstValue(ClaimTypes.Name);
        var ticketUid = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (identity is null ||
            !string.Equals(ticketName, identity.Name, StringComparison.Ordinal) ||
            !uint.TryParse(ticketUid, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) ||
            uid != identity.Uid)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }

        var claims = context.Principal!.Claims.ToArray();
        var mustSanitizeTicket = claims.Length != 2 ||
            claims.Count(claim => claim.Type == ClaimTypes.Name && claim.Value == identity.Name) != 1 ||
            claims.Count(claim => claim.Type == ClaimTypes.NameIdentifier && claim.Value == ticketUid) != 1;
        context.ReplacePrincipal(ServalAuthentication.CreatePrincipal(identity));
        context.ShouldRenew = mustSanitizeTicket;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(ServalAuthentication.Scheme).ConfigureAwait(false);
    }
}

public interface IAuthenticatedTicketAccessor
{
    Task<string?> GetSessionAsync(HttpContext context);
}

public sealed class AuthenticatedTicketAccessor : IAuthenticatedTicketAccessor
{
    public async Task<string?> GetSessionAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await context.AuthenticateAsync(ServalAuthentication.Scheme).ConfigureAwait(false);
        return result.Succeeded &&
            result.Properties?.Items.TryGetValue(ServalAuthentication.SessionProperty, out var session) == true &&
            !string.IsNullOrWhiteSpace(session)
                ? session
                : null;
    }
}

public interface IInvalidSessionHandler
{
    Task<IActionResult> ChallengeAsync(HttpContext context);
}

public sealed class InvalidSessionHandler : IInvalidSessionHandler
{
    public async Task<IActionResult> ChallengeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.SignOutAsync(ServalAuthentication.Scheme).ConfigureAwait(false);
        return new ChallengeResult(ServalAuthentication.Scheme);
    }
}
