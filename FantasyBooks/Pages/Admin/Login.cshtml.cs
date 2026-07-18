using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FantasyBooks.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace FantasyBooks.Pages.Admin;

[AllowAnonymous]
public class LoginModel(IOptions<AdminOptions> adminOptions) : PageModel
{
    private readonly AdminOptions _admin = adminOptions.Value;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public string? ReturnUrl { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Admin/Products/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return Page();

        var expectedUser = _admin.Username?.Trim() ?? "";
        var expectedPassword = _admin.Password ?? "";

        var userOk = string.Equals(Input.Username?.Trim(), expectedUser, StringComparison.Ordinal);
        var passwordOk = userOk && !string.IsNullOrEmpty(expectedPassword)
            && string.Equals(Input.Password, expectedPassword, StringComparison.Ordinal);

        if (!passwordOk)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, expectedUser),
            new(ClaimTypes.Role, "Admin"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = Input.RememberMe,
                RedirectUri = returnUrl,
            });

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Admin/Products/Index");
    }

    public class InputModel
    {
        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; } = true;
    }
}
