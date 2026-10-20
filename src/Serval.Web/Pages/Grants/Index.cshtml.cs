using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Grants;

public sealed class IndexModel(
    IAgentIpcClient agent,
    IAuthenticatedTicketAccessor ticketAccessor,
    IInvalidSessionHandler invalidSession) : PageModel
{
    [BindProperty]
    public string SubjectKind { get; set; } = "User";

    [BindProperty]
    public string Subject { get; set; } = string.Empty;

    [BindProperty]
    public string Service { get; set; } = string.Empty;

    [BindProperty]
    public long GrantId { get; set; }

    public IReadOnlyList<AgentGrant> Grants { get; private set; } = [];

    public string? Error { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        var session = await ticketAccessor.GetSessionAsync(HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        var result = (AgentResult.AddGrant)await agent.SendAsync(
            new AgentRequest.AddGrant(Guid.NewGuid().ToString("N"), session,
                SubjectKind, Subject, "Service.View", Service), cancellationToken);
        if (result.Code == AgentResultCode.Success)
        {
            return RedirectToPage();
        }

        if (result.Code == AgentResultCode.Unauthenticated)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        Error = "Grant could not be added.";
        return await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        var session = await ticketAccessor.GetSessionAsync(HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        var result = (AgentResult.RemoveGrant)await agent.SendAsync(
            new AgentRequest.RemoveGrant(Guid.NewGuid().ToString("N"), session, GrantId),
            cancellationToken);
        if (result.Code == AgentResultCode.Success)
        {
            return RedirectToPage();
        }

        if (result.Code == AgentResultCode.Unauthenticated)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        Error = "Grant could not be removed.";
        return await LoadAsync(cancellationToken);
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        var session = await ticketAccessor.GetSessionAsync(HttpContext).ConfigureAwait(false);
        if (session is null)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        var result = (AgentResult.ListGrants)await agent.SendAsync(
            new AgentRequest.ListGrants(Guid.NewGuid().ToString("N"), session), cancellationToken);
        if (result.Code == AgentResultCode.Unauthenticated)
        {
            return await invalidSession.ChallengeAsync(HttpContext).ConfigureAwait(false);
        }

        if (result.Code == AgentResultCode.Denied)
        {
            return StatusCode(403);
        }

        if (result.Code == AgentResultCode.Success && result.Grants is not null)
        {
            Grants = result.Grants;
        }
        else
        {
            Error = "Grants are temporarily unavailable.";
        }

        return Page();
    }
}
