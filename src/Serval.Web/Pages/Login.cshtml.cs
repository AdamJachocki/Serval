using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serval.Application.Ipc;

namespace Serval.Web.Pages;

public sealed class LoginModel(IAgentIpcClient agent) : PageModel
{
    [BindProperty]
    [Display(Name = "Nazwa użytkownika")]
    [Required(ErrorMessage = "Podaj nazwę użytkownika.")]
    [StringLength(128, ErrorMessage = "Nazwa użytkownika może mieć najwyżej 128 znaków.")]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Display(Name = "Hasło")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [StringLength(1024, ErrorMessage = "Hasło może mieć najwyżej 1024 znaki.")]
    public string? Password { get; set; }

    public string? Error { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ClearPasswordFromReturnedForm();
            return Page();
        }

        var result = (AgentResult.Login)await agent.SendAsync(
            new AgentRequest.Login(Guid.NewGuid().ToString("N"), Username, Password ?? string.Empty),
            cancellationToken);
        ClearPasswordFromReturnedForm();
        if (result.Code == AgentResultCode.Success && !string.IsNullOrEmpty(result.Session))
        {
            ServalSessionCookie.Set(Response, result.Session);
            return RedirectToPage("/Services/Index");
        }

        Error = "Nie udało się zalogować. Sprawdź dane i spróbuj ponownie.";
        return Page();
    }

    private void ClearPasswordFromReturnedForm()
    {
        Password = string.Empty;
        if (!ModelState.TryGetValue(nameof(Password), out var entry))
        {
            return;
        }

        var errors = entry.Errors.Select(error => error.ErrorMessage).ToArray();
        ModelState.Remove(nameof(Password));
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(Password), error);
        }
    }
}
