using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class DashboardModel : AppPageModel
{
    public string UserEmail { get; set; }

    public IActionResult OnGet()
    {
        UserEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(UserEmail))
        {
            // Not authenticated, redirect to login
            return RedirectToPage("/Login");
        }

        return Page();
    }
}