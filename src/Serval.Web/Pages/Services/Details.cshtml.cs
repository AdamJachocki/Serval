using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

public sealed class DetailsModel(IAgentIpcClient agent) : PageModel
{
    public async Task<IActionResult> OnGetEnvironmentAsync(string service, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var session = ServalSessionCookie.Read(Request);
        if (session is null) return Unauthorized();
        var result = (AgentResult.ListEnvironment)await agent.SendAsync(
            new AgentRequest.ListEnvironment(Guid.NewGuid().ToString("N"), session, service), cancellationToken);
        if (result.Code == AgentResultCode.Success && result.Environment is not null)
            return new JsonResult(result.Environment);
        return EnvironmentFailure(result.Code, result.Failure);
    }

    public async Task<IActionResult> OnPostRevealAsync(string service, string name, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var session = ServalSessionCookie.Read(Request);
        if (session is null) return Unauthorized();
        using var result = (AgentResult.RevealEnvironmentVariable)await agent.SendAsync(
            new AgentRequest.RevealEnvironmentVariable(Guid.NewGuid().ToString("N"), session, service, name), cancellationToken);
        if (result.Code != AgentResultCode.Success || result.Variable is null)
            return EnvironmentFailure(result.Code, result.Failure);
        Response.ContentType = "application/json; charset=utf-8";
        await result.Variable.WriteHttpAsync(Response.Body, cancellationToken);
        return new EmptyResult();
    }

    private IActionResult EnvironmentFailure(AgentResultCode code, string? failure)
    {
        if (code == AgentResultCode.Unauthenticated)
        {
            ServalSessionCookie.Clear(Response);
            return Unauthorized();
        }
        if (code is AgentResultCode.NotFound or AgentResultCode.Denied or AgentResultCode.InvalidRequest)
            return NotFound();
        var message = failure switch
        {
            "UnsupportedConfiguration" => "Ta konfiguracja nie jest obsługiwana.",
            "InconsistentSnapshot" => "Konfiguracja zmieniła się podczas odczytu. Spróbuj ponownie.",
            "LimitExceeded" => "Przekroczono limit rozmiaru odczytu.",
            _ => "Środowisko jest chwilowo niedostępne.",
        };
        return new JsonResult(new { error = message }) { StatusCode = 503 };
    }

    public AgentService? Service { get; private set; }

    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "state")]
    public string? State { get; set; }

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

        if (result.Code is AgentResultCode.NotFound or AgentResultCode.Denied or
            AgentResultCode.InvalidRequest)
        {
            return NotFound();
        }

        if (!BrowsingContextIsValid())
        {
            return BadRequest("Nieprawidłowe parametry filtrowania.");
        }

        if (result.Code == AgentResultCode.Success && result.Service is not null)
        {
            Service = result.Service;
            return Page();
        }

        Error = "Szczegóły usługi są chwilowo niedostępne.";
        return Page();
    }

    private bool BrowsingContextIsValid() =>
        Request.Query["q"].Count <= 1 &&
        Request.Query["state"].Count <= 1 &&
        (Query?.Length ?? 0) <= IndexModel.MaximumSearchLength &&
        (State?.Length ?? 0) <= IndexModel.MaximumStateLength;
}
