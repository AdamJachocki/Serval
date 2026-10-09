using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class IndexModel(IAgentIpcClient agent) : PageModel
{
    public IReadOnlyList<AgentService> Services { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var session = ServalSessionCookie.Read(Request);
        if (session is null)
        {
            return RedirectToPage("/Login");
        }

        var result = (AgentResult.ListServices)await agent.SendAsync(
            new AgentRequest.ListServices(Guid.NewGuid().ToString("N"), session), cancellationToken);
        if (result.Code == AgentResultCode.Unauthenticated)
        {
            ServalSessionCookie.Clear(Response);
            return RedirectToPage("/Login");
        }

        if (result.Code == AgentResultCode.Success && result.Services is not null)
        {
            Services = result.Services;
        }
        else
        {
            Error = "Services are temporarily unavailable.";
        }

        return Page();
    }
}
