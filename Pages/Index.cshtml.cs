using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FantasyToolbox.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }

        public string UserEmail { get; set; }

        public IActionResult OnGet()
        {

            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (!string.IsNullOrEmpty(userEmail))
            {
                // Authenticated user, redirect to dashboard
                return RedirectToPage("/Dashboard");
            }
            return Page();
            //UserEmail = HttpContext.Session.GetString("UserEmail");
        }

        public IActionResult OnPostLogout()
        {
            HttpContext.Session.Remove("UserEmail");
            return RedirectToPage("/Login");
        }

       
    }
}
