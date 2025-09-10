using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    [BindProperty]
    public string Action { get; set; } // "login"

    public string Message { get; set; }

    [BindProperty]
    public bool RememberMe { get; set; } // Add RememberMe property

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }

    public IActionResult OnGet()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        RememberMe = true; // Default to checked
        if (!string.IsNullOrEmpty(userEmail))
        {
            // Already authenticated, redirect to dashboard
            return RedirectToPage("/Dashboard");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Message = "Please enter a valid email and password.";
            return Page();
        }

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        using var loginCmd = new NpgsqlCommand(
            "SELECT passwordhash, isactive FROM users WHERE email = @email", conn);
        loginCmd.Parameters.AddWithValue("email", Input.Email.ToLowerInvariant().Trim());

        using var reader = await loginCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            Message = "Login is invalid.";
            return Page();
        }

        var dbPassword = reader["passwordhash"] as string;
        var isActive = reader["isactive"] is bool b && b;

        if (!isActive)
        {
            Message = "Account is inactive. Please contact support.";
            return Page();
        }

        var hasher = new PasswordHasher<string>();
        var result = dbPassword != null ? hasher.VerifyHashedPassword(null, dbPassword, Input.Password) : PasswordVerificationResult.Failed;

        if (result != PasswordVerificationResult.Success)
        {
            Message = "Login is invalid.";
            return Page();
        }

        // Update lastlogin
        reader.Close();
        using var updateCmd = new NpgsqlCommand("UPDATE users SET lastlogin = NOW() WHERE email = @email", conn);
        updateCmd.Parameters.AddWithValue("email", Input.Email.ToLowerInvariant().Trim());
        await updateCmd.ExecuteNonQueryAsync();

        // Set session
        HttpContext.Session.SetString("UserEmail", Input.Email.ToLowerInvariant().Trim());

        // Set persistent authentication cookie
        var claims = new[] { new Claim(ClaimTypes.Name, Input.Email.ToLowerInvariant().Trim()) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = RememberMe, // true if "Remember Me" checked
            ExpiresUtc = RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(1)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        Message = "Login successful!";
        return RedirectToPage("/Dashboard");
    }
}