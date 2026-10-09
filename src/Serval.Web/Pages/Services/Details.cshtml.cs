using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class DetailsModel(IAgentIpcClient agent) : PageModel
{
    public AgentService? Service { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string service, CancellationToken cancellationToken)
    {
        var session = ServalSessionCookie.Read(Request);
        if (session is null)
        {
            return RedirectToPage("/Login");
        }

        var result = (AgentResult.InspectService)await agent.SendAsync(
            new AgentRequest.InspectService(Guid.NewGuid().ToString("N"), session, service),
            cancellationToken);
        if (result.Code == AgentResultCode.Unauthenticated)
        {
            ServalSessionCookie.Clear(Response);
            return RedirectToPage("/Login");
        }

        if (result.Code == AgentResultCode.Success && result.Service is not null)
        {
            Service = result.Service;
            return Page();
        }

        if (result.Code is AgentResultCode.NotFound or AgentResultCode.Denied or
            AgentResultCode.InvalidRequest)
        {
            return NotFound();
        }

        Error = "Service details are temporarily unavailable.";
        return Page();
    }
}
