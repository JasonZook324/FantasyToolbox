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
        
        UserEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(UserEmail))
        {
            // Not authenticated, redirect to login
            return RedirectToPage("/Login");
        }
        try
        {
            CurrentUser = await _userService.GetUserByEmailAsync(UserEmail);
            if (CurrentUser == null)
            {
                // User not found, redirect to login
                _logger.LogAsync($"User with email {UserEmail} not found in Dashboard OnGetAsync.").GetAwaiter().GetResult();
                return RedirectToPage("/Login");
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
