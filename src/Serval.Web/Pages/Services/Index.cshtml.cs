using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class IndexModel(IAgentIpcClient agent) : PageModel
{
    public const int MaximumSearchLength = 256;
    public const int MaximumStateLength = 64;
    public const int MaximumServiceLength = 255;
    public const string GenericFavoriteFailure = "Nie udało się zmienić ulubionych usług.";
    public const string FavoriteLimitMessage = "Osiągnięto limit 100 ulubionych usług. Usuń którąś, aby dodać kolejną.";

    private HashSet<string> favoriteIds = new(StringComparer.Ordinal);

    public IReadOnlyList<AgentService> Services { get; private set; } = [];

    public IReadOnlyList<AgentService> Favorites { get; private set; } = [];

    public IReadOnlyList<AgentService> Others { get; private set; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public string? FavoriteMessage { get; private set; }

    [BindProperty(SupportsGet = true, Name = "favorite")]
    public string? FavoriteStatus { get; set; }

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

        if (result.Code == AgentResultCode.Success && result.Services is not null &&
            TryReadFavorites(result, out var favoriteSet))
        {
            Services = result.Services;
            favoriteIds = favoriteSet;
            Favorites = Services.Where(service => favoriteIds.Contains(service.Id)).ToArray();
            Others = Services.Where(service => !favoriteIds.Contains(service.Id)).ToArray();
            FavoriteMessage = FavoriteStatus switch
            {
                "limit" => FavoriteLimitMessage,
                "failed" => GenericFavoriteFailure,
                _ => null,
            };
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

    public async Task<IActionResult> OnPostFavoriteAsync(
        string? service, string? action, CancellationToken cancellationToken)
    {
        var session = ServalSessionCookie.Read(Request);
        if (session is null)
        {
            return RedirectToPage("/Login");
        }

        var query = SingleBoundedFormValue("q", MaximumSearchLength);
        var state = SingleBoundedFormValue("state", MaximumStateLength);
        if (Request.Form["service"].Count != 1 || service is not { Length: > 0 and <= MaximumServiceLength } ||
            action is not ("add" or "remove"))
        {
            return BackToList(query, state, "failed");
        }

        var correlation = Guid.NewGuid().ToString("N");
        AgentRequest request = action == "add"
            ? new AgentRequest.AddFavorite(correlation, session, service)
            : new AgentRequest.RemoveFavorite(correlation, session, service);
        var result = await agent.SendAsync(request, cancellationToken);
        switch (result.Code)
        {
            case AgentResultCode.Success:
                return BackToList(query, state, null);
            case AgentResultCode.Unauthenticated:
                ServalSessionCookie.Clear(Response);
                return RedirectToPage("/Login");
            case AgentResultCode.LimitReached:
                return BackToList(query, state, "limit");
            default:
                return BackToList(query, state, "failed");
        }
    }

    public bool IsFavorite(AgentService service) => favoriteIds.Contains(service.Id);

    private RedirectToPageResult BackToList(string? query, string? state, string? favorite) =>
        RedirectToPage("/Services/Index", new { q = query, state, favorite });

    private string? SingleBoundedFormValue(string name, int maximumLength)
    {
        var values = Request.Form[name];
        return values.Count == 1 && values[0] is { Length: > 0 } value && value.Length <= maximumLength
            ? value : null;
    }

    private static bool TryReadFavorites(AgentResult.ListServices result, out HashSet<string> favorites)
    {
        favorites = new HashSet<string>(StringComparer.Ordinal);
        var known = result.Services!.Select(service => service.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in result.Favorites ?? [])
        {
            if (!known.Contains(id))
            {
                return false;
            }

            favorites.Add(id);
        }

        return true;
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
