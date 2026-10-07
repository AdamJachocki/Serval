using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages;

public sealed class LogoutModel(IAgentIpcClient agent) : PageModel
{
    public string? Error { get; private set; }

    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var session = ServalSessionCookie.Read(Request);
        if (session is null)
        {
            ServalSessionCookie.Clear(Response);
            return RedirectToPage("/Login");
        }

        var result = (AgentResult.Logout)await agent.SendAsync(
            new AgentRequest.Logout(Guid.NewGuid().ToString("N"), session), cancellationToken);
        if (result.Code is AgentResultCode.Success or AgentResultCode.Unauthenticated)
        {
            ServalSessionCookie.Clear(Response);
            return RedirectToPage("/Login");
        }

        Error = "Sign out could not be confirmed. Try again.";
        return Page();
    }
}
