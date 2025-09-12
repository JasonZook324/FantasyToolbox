using Microsoft.AspNetCore.Mvc;

public class DashboardModel : AppPageModel
{
    private readonly IUserService _userService;
    private readonly ILogService _logger;
    private readonly IESPNService _ESPNService;

    public string? UserEmail { get; set; }
    public User? CurrentUser { get; set; }

    public DashboardModel(IESPNService espnService, IUserService userService, ILogService logger)
        : base(logger, espnService)
    {
        _userService = userService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        
        // Try to get user email from session first
        UserEmail = HttpContext.Session.GetString("UserEmail");
        
        // If session is empty, try to get from authentication claims as fallback
        if (string.IsNullOrEmpty(UserEmail) && User.Identity?.IsAuthenticated == true)
        {
            UserEmail = User.Identity.Name;
        }
        
        if (string.IsNullOrEmpty(UserEmail))
        {
            // Not authenticated, redirect to login with clear session
            HttpContext.Session.Clear();
            return RedirectToPage("/Login", new { message = "Please log in to access your dashboard." });
        }
        
        try
        {
            CurrentUser = await _userService.GetUserByEmailAsync(UserEmail);
            if (CurrentUser == null)
            {
                // User not found, clear session and redirect to login
                HttpContext.Session.Clear();
                _logger.LogAsync($"User with email {UserEmail} not found in Dashboard OnGetAsync.").GetAwaiter().GetResult();
                return RedirectToPage("/Login", new { message = "User account not found. Please log in again." });
            }
            
            // Make sure session has user email for future requests
            if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserEmail")))
            {
                HttpContext.Session.SetString("UserEmail", CurrentUser.Email);
                HttpContext.Session.SetString("FirstName", CurrentUser.FirstName ?? "");
                HttpContext.Session.SetString("LastName", CurrentUser.LastName ?? "");
            }
           
        }
        catch (Exception ex)
        {
            _logger.LogAsync($"Error in Dashboard OnGetAsync: {ex.Message}").GetAwaiter().GetResult();
            return RedirectToPage("/Error");
        }
        return Page();
    }
}
