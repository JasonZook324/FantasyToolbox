using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

public class LoginModel : PageModel
{
    private readonly IUserService _userService;
    private readonly ILogService _logger;

    public LoginModel(IUserService userService, ILogService logger)
    {
        _userService = userService;
        _logger = logger;
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

    public IActionResult OnGet(string? message = null)
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        RememberMe = true;
        
        // Display message from redirects (like from Dashboard authentication failures)
        if (!string.IsNullOrEmpty(message))
        {
            Message = message;
        }
        
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

        User? user = null;
        
        // Database operations
        try
        {
            user = await _userService.GetUserByEmailAsync(Input.Email);
            if (user == null)
            {
                Message = "Login is invalid.";
                _logger.LogAsync($"Failed login attempt for email: {Input.Email}").GetAwaiter().GetResult();
                return Page();
            }

            if (!user.IsActive)
            {
                Message = "Account is inactive. Please contact support.";
                _logger.LogAsync($"Inactive account login attempt for email: {Input.Email}").GetAwaiter().GetResult();
                return Page();
            }

            var hasher = new PasswordHasher<string>();
            var result = hasher.VerifyHashedPassword(null, user.PasswordHash, Input.Password);

            if (result != PasswordVerificationResult.Success)
            {
                Message = "Login is invalid.";
                _logger.LogAsync($"Failed login attempt for email: {Input.Email}").GetAwaiter().GetResult();
                return Page();
            }

            // Check if email is verified
            if (!user.EmailVerified)
            {
                Message = "Please verify your email address before logging in. Check your inbox for a verification code.";
                _logger.LogAsync($"Login attempt with unverified email: {Input.Email}").GetAwaiter().GetResult();
                return RedirectToPage("/VerifyEmail", new { email = user.Email });
            }

            await _userService.UpdateLastLoginAsync(user);
        }
        catch (Exception ex)
        {
            Message = "Database connection issue. Please try again in a moment.";
            await _logger.LogAsync($"Database error during login for email: {Input.Email}. Exception: {ex.Message}", "Error", ex.ToString());
            return Page();
        }

        // Session and authentication setup
        try
        {
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

            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, authProperties);
            
            // Log successful login and redirect
            await _logger.LogAsync($"Successful login for email: {user.Email}", "Info");
            return RedirectToPage("/Dashboard");
        }
        catch (Exception ex)
        {
            Message = "Authentication system issue. Please try logging in again.";
            await _logger.LogAsync($"Authentication error during login for email: {user.Email}. Exception: {ex.Message}", "Error", ex.ToString());
            return Page();
        }

    }
}