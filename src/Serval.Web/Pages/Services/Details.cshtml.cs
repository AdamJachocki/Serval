using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class DetailsModel(
    IAgentIpcClient agent,
    IAuthenticatedTicketAccessor ticketAccessor,
    IInvalidSessionHandler invalidSession) : PageModel
{
    public AgentService? Service { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string service, CancellationToken cancellationToken)
    {
        var session = await ticketAccessor.GetSessionAsync(HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        var result = (AgentResult.InspectService)await agent.SendAsync(
            new AgentRequest.InspectService(Guid.NewGuid().ToString("N"), session, service),
            cancellationToken);
        if (result.Code == AgentResultCode.Unauthenticated)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
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
