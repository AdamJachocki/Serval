namespace Serval.Web.Presentation;

public sealed record ServiceStatusPresentation(string Label, string Modifier, string Symbol)
{
    public static ServiceStatusPresentation From(string? activeState) => activeState switch
    {
        "active" => new("Aktywna", "active", "✓"),
        "inactive" => new("Nieaktywna", "inactive", "‖"),
        "failed" => new("Błąd", "failed", "!"),
        "activating" => new("Aktywacja", "transitional", "↻"),
        "deactivating" => new("Dezaktywacja", "transitional", "↻"),
        "reloading" => new("Przeładowanie", "transitional", "↻"),
        null or "" => new("Nieznany stan", "unknown", "?"),
        _ => new(activeState, "unknown", "?"),
    };
}
