using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;

public class DashboardModel : AppPageModel
{
    private readonly IUserService _userService;

    public string UserEmail { get; set; }
    public User CurrentUser { get; set; }

   
    public DashboardModel(IEspnSessionService espnSessionService, IUserService userService) : base(espnSessionService)
    {
        _userService = userService;
    }
    

    public async Task<IActionResult> OnGetAsync()
    {
        UserEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(UserEmail))
        {
            // Not authenticated, redirect to login
            return RedirectToPage("/Login");
        }

        CurrentUser = await _userService.GetUserByEmailAsync(UserEmail);
        if (CurrentUser == null)
        {
            // User not found, redirect to login
            return RedirectToPage("/Login");
        }

        return Page();
    }
}
