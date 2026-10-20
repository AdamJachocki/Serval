using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages;

public sealed class LogoutModel(
    IAgentIpcClient agent,
    IAuthenticatedTicketAccessor ticketAccessor,
    IInvalidSessionHandler invalidSession) : PageModel
{
    public string? Error { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var session = await ticketAccessor.GetSessionAsync(HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        var result = (AgentResult.Logout)await agent.SendAsync(
            new AgentRequest.Logout(Guid.NewGuid().ToString("N"), session), cancellationToken);
        if (result.Code is AgentResultCode.Success or AgentResultCode.Unauthenticated)
        {
            await HttpContext.SignOutAsync(ServalAuthentication.Scheme).ConfigureAwait(false);
            return RedirectToPage("/Login");
        }

        Error = "Sign out could not be confirmed. Try again.";
        return Page();
    }
}
