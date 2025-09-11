using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

public class RegisterModel : PageModel
{
    private readonly IUserService _userService;
    private readonly ILogService _logger;

    public RegisterModel(IUserService userService, ILogService logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    public string Message { get; set; }

    public class InputModel
    {
        [Required]
        [StringLength(50, MinimumLength = 2)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 2)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Message = "Please correct the errors and try again.";
            _logger.LogAsync("Invalid registration attempt.").GetAwaiter().GetResult();
            return Page();
        }

        var email = Input.Email.ToLowerInvariant().Trim();
        try
        {
            var exists = await _userService.UserExistsAsync(email);
        }
        catch (Exception ex) 
        {
            _logger.LogAsync("Error checking user existence: " + ex.Message, "Error", ex.StackTrace).GetAwaiter().GetResult();
            Message = "Account already exists or invalid input.";
            return Page();
        }

        
       

        var hasher = new PasswordHasher<string>();
        var hashedPassword = hasher.HashPassword(null, Input.Password);

        var user = new User
        {
            FirstName = Input.FirstName.Trim(),
            LastName = Input.LastName.Trim(),
            Email = email,
            PasswordHash = hashedPassword,
            IsActive = true
        };

        try
        {
            await _userService.CreateUserAsync(user);
            Message = "Account created. Please log in.";
            return RedirectToPage("/Login");
        } catch (Exception ex) 
        {
            _logger.LogAsync("Error creating user: " + ex.Message, "Error", ex.Message).GetAwaiter().GetResult();
            Message = "Error creating account. Please try again." + ex.Message;
            
        }
        return Page();
    }
}