using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

public class LoginModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public LoginModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    [BindProperty]
    public string Action { get; set; }

    public string Message { get; set; }

    [BindProperty]
    public bool RememberMe { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        public string Password { get; set; }
    }

    public IActionResult OnGet()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        RememberMe = true;
        if (!string.IsNullOrEmpty(userEmail))
        {
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

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == Input.Email.ToLowerInvariant().Trim());

        if (user == null)
        {
            Message = "Login is invalid.";
            return Page();
        }

        if (!user.IsActive)
        {
            Message = "Account is inactive. Please contact support.";
            return Page();
        }

        var hasher = new PasswordHasher<string>();
        var result = hasher.VerifyHashedPassword(null, user.PasswordHash, Input.Password);

        if (result != PasswordVerificationResult.Success)
        {
            Message = "Login is invalid.";
            return Page();
        }

        user.LastLogin = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        HttpContext.Session.SetString("UserEmail", user.Email);
        HttpContext.Session.SetString("FirstName", user.FirstName ?? "");
        HttpContext.Session.SetString("LastName", user.LastName ?? "");

        var claims = new[] { new Claim(ClaimTypes.Name, user.Email) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = RememberMe,
            ExpiresUtc = RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(1)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        Message = "Login successful!";
        return RedirectToPage("/Dashboard");
    }
}