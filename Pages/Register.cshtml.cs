using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.AspNetCore.Identity;

public class RegisterModel : PageModel
{
    private readonly IConfiguration _configuration;

    public RegisterModel(IConfiguration configuration)
    {
        _configuration = configuration;
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
            return Page();
        }

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        using var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM users WHERE email = @email", conn);
        checkCmd.Parameters.AddWithValue("email", Input.Email.ToLowerInvariant().Trim());
        var exists = (long)await checkCmd.ExecuteScalarAsync();

        if (exists > 0)
        {
            Message = "Account already exists or invalid input.";
            return Page();
        }

        var hasher = new PasswordHasher<string>();
        var hashedPassword = hasher.HashPassword(null, Input.Password);

        using var insertCmd = new NpgsqlCommand(
            "INSERT INTO users (firstname, lastname, email, passwordhash) VALUES (@firstname, @lastname, @email, @passwordhash)", conn);
        insertCmd.Parameters.AddWithValue("firstname", Input.FirstName.Trim());
        insertCmd.Parameters.AddWithValue("lastname", Input.LastName.Trim());
        insertCmd.Parameters.AddWithValue("email", Input.Email.ToLowerInvariant().Trim());
        insertCmd.Parameters.AddWithValue("passwordhash", hashedPassword);
        await insertCmd.ExecuteNonQueryAsync();

        Message = "Account created. Please log in.";
        return RedirectToPage("/Login");
    }
}