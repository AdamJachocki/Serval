using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
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

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ClearPassword();
            return Page();
        }

        var result = (AgentResult.Login)await agent.SendAsync(
            new AgentRequest.Login(Guid.NewGuid().ToString("N"), Username, Password),
            cancellationToken);
        ClearPassword();
        if (result.Code == AgentResultCode.Success && !string.IsNullOrEmpty(result.Session) &&
            result.Identity is not null)
        {
            var properties = new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
            };
            properties.Items[ServalAuthentication.SessionProperty] = result.Session;
            await HttpContext.SignInAsync(ServalAuthentication.Scheme,
                ServalAuthentication.CreatePrincipal(result.Identity), properties).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(ReturnUrl);
            }

            return RedirectToPage("/Services/Index");
        }

        Error = result.Code == AgentResultCode.Unavailable
            ? "Sign in is temporarily unavailable. Try again later."
            : "Sign in failed. Check your credentials.";
        return Page();
    }

    private void ClearPassword()
    {
        Password = string.Empty;
        ModelState.Remove(nameof(Password));
    }
}
