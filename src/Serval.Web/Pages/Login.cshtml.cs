using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages;

public sealed class LoginModel(IAgentIpcClient agent) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(128)]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(1024)]
    public string Password { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = (AgentResult.Login)await agent.SendAsync(
            new AgentRequest.Login(Guid.NewGuid().ToString("N"), Username, Password),
            cancellationToken);
        Password = string.Empty;
        if (result.Code == AgentResultCode.Success && !string.IsNullOrEmpty(result.Session))
        {
            ServalSessionCookie.Set(Response, result.Session);
            return RedirectToPage("/Services/Index");
        }

        Error = "Sign in failed. Check your credentials or try again later.";
        return Page();
    }
}
