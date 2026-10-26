using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class IndexModel(IAgentIpcClient agent) : PageModel
{
    public const int MaximumSearchLength = 256;
    public const int MaximumStateLength = 64;

    public IReadOnlyList<AgentService> Services { get; private set; } = [];

    public IReadOnlyList<AgentService> FilteredServices { get; private set; } = [];

    public IReadOnlyList<string> AvailableStates { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "state")]
    public string? State { get; set; }

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

        if (!FiltersAreValid())
        {
            return BadRequest("Nieprawidłowe parametry filtrowania.");
        }

        if (result.Code == AgentResultCode.Success && result.Services is not null)
        {
            Services = result.Services;
            AvailableStates = Services
                .Select(service => service.ActiveState)
                .Where(state => !string.IsNullOrEmpty(state))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            FilteredServices = Services.Where(MatchesFilters).ToArray();
        }
        else
        {
            Error = "Lista usług jest chwilowo niedostępna.";
        }

        return Page();
    }

    private bool FiltersAreValid() =>
        Request.Query["q"].Count <= 1 &&
        Request.Query["state"].Count <= 1 &&
        (Query?.Length ?? 0) <= MaximumSearchLength &&
        (State?.Length ?? 0) <= MaximumStateLength;

    private bool MatchesFilters(AgentService service)
    {
        var matchesText = string.IsNullOrEmpty(Query) ||
            service.Id.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
            (service.Description?.Contains(Query, StringComparison.OrdinalIgnoreCase) ?? false);
        var matchesState = string.IsNullOrEmpty(State) ||
            string.Equals(service.ActiveState, State, StringComparison.Ordinal);
        return matchesText && matchesState;
    }
}
