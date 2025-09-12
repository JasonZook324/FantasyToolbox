using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using FantasyToolbox.Services;

public class VerifyEmailModel : PageModel
{
    private readonly IUserService _userService;
    private readonly IEmailService _emailService;
    private readonly ILogService _logger;

    public VerifyEmailModel(IUserService userService, IEmailService emailService, ILogService logger)
    {
        _userService = userService;
        _emailService = emailService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; }

    public string Message { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Verification code must be exactly 6 digits.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Verification code must contain only numbers.")]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Register");
        }

        var user = await _userService.GetUserByEmailAsync(Email);
        if (user == null)
        {
            return RedirectToPage("/Register");
        }

        if (user.EmailVerified)
        {
            return RedirectToPage("/Login", new { message = "Your email is already verified. Please log in." });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Register");
        }

        var user = await _userService.GetUserByEmailAsync(Email);
        if (user == null)
        {
            Message = "User not found. Please register again.";
            return Page();
        }

        if (user.EmailVerified)
        {
            return RedirectToPage("/Login", new { message = "Your email is already verified. Please log in." });
        }

        try
        {
            var canResend = await _userService.CanResendVerificationCodeAsync(user);
            if (!canResend)
            {
                Message = "Please wait at least 1 minute before requesting a new verification code.";
                return Page();
            }

            var verificationCode = await _userService.GenerateVerificationCodeAsync(user);
            var emailSent = await _emailService.SendVerificationEmailAsync(Email, user.FirstName, verificationCode);
            
            if (emailSent)
            {
                Message = "A new verification code has been sent to your email.";
            }
            else
            {
                Message = "There was an issue sending the verification email. Please try again or contact support.";
            }
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Error resending verification code to {Email}: {ex.Message}", "Error", ex.ToString());
            Message = "There was an error resending the verification code. Please try again.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Email))
        {
            return RedirectToPage("/Register");
        }

        if (!ModelState.IsValid)
        {
            Message = "Please enter a valid 6-digit verification code.";
            return Page();
        }

        try
        {
            var user = await _userService.GetUserByEmailAsync(Email);
            if (user == null)
            {
                Message = "User not found. Please register again.";
                return Page();
            }

            if (user.EmailVerified)
            {
                return RedirectToPage("/Login", new { message = "Your email is already verified. Please log in." });
            }

            var isValid = await _userService.VerifyEmailCodeAsync(Email, Input.VerificationCode);
            
            if (isValid)
            {
                await _userService.SetEmailVerifiedAsync(user);
                await _logger.LogAsync($"Email verified successfully for user: {Email}");
                
                return RedirectToPage("/Login", new { message = "Email verified successfully! You can now log in." });
            }
            else
            {
                Message = "Invalid or expired verification code. Please check your email for the correct code or request a new one.";
                await _logger.LogAsync($"Invalid verification attempt for email: {Email}");
                return Page();
            }
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Error verifying email for {Email}: {ex.Message}", "Error", ex.ToString());
            Message = "There was an error verifying your email. Please try again.";
            return Page();
        }
    }
}