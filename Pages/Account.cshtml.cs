using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

[Authorize]
public class AccountModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public AccountModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    [BindProperty]
    public int? SelectedTeamId { get; set; }

    public List<TeamInfo> Teams { get; set; } = new();

    public string Message { get; set; }
    public string TeamMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "New Email")]
        public string NewEmail { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; set; }
    }

    public class TeamInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrEmpty(userEmail))
            return RedirectToPage("/Login");

        Input = new InputModel { NewEmail = userEmail };

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
            return RedirectToPage("/Login");

        SelectedTeamId = user.SelectedTeamId;

        var auth = await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        var leagueData = await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == user.UserId);

        string swid = auth?.Swid;
        string espn_s2 = auth?.EspnS2;
        string leagueId = leagueData?.LeagueId;
        int seasonYear = leagueData?.LeagueYear ?? 0;

        if (!string.IsNullOrEmpty(swid) && !string.IsNullOrEmpty(espn_s2) && !string.IsNullOrEmpty(leagueId) && seasonYear > 0)
        {
            try
            {
                using var handler = new HttpClientHandler { UseCookies = false };
                using var httpClient = new HttpClient(handler);
                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{seasonYear}/segments/0/leagues/{leagueId}?view=mTeam";
                httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={swid}; espn_s2={espn_s2}");
                var response = await httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var doc = JObject.Parse(json);
                    var teams = doc["teams"];
                    if (teams != null)
                    {
                        foreach (var team in teams)
                        {
                            var teamId = (int)team["id"];
                            var teamName = team["name"]?.ToString() ?? "";
                            var abbv = team["abbrev"]?.ToString() ?? "";
                            Teams.Add(new TeamInfo { Id = teamId, Name = teamName + " (" + abbv + ")" });
                        }
                    }
                }
                else
                {
                    TeamMessage = $"Failed to fetch teams from ESPN API. Status: {(int)response.StatusCode} {response.ReasonPhrase}";
                }
            }
            catch
            {
                TeamMessage = "Error connecting to ESPN API for teams.";
            }
        }
        else
        {
            TeamMessage = "Missing ESPN credentials or league info.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrEmpty(userEmail))
            return RedirectToPage("/Login");

        if (!ModelState.IsValid)
        {
            Message = "Please correct the errors and try again.";
            await OnGetAsync();
            return Page();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
            return RedirectToPage("/Login");

        var dbPassword = user.PasswordHash;
        var hasher = new PasswordHasher<string>();
        var result = dbPassword != null ? hasher.VerifyHashedPassword(null, dbPassword, Input.CurrentPassword) : PasswordVerificationResult.Failed;

        if (result != PasswordVerificationResult.Success)
        {
            Message = "Current password is incorrect.";
            await OnGetAsync();
            return Page();
        }

        user.Email = Input.NewEmail.ToLowerInvariant().Trim();
        if (!string.IsNullOrEmpty(Input.NewPassword))
        {
            user.PasswordHash = hasher.HashPassword(null, Input.NewPassword);
        }
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();

        if (Input.NewEmail.ToLowerInvariant().Trim() != userEmail.ToLowerInvariant().Trim())
        {
            var claims = new[] { new Claim(ClaimTypes.Name, Input.NewEmail.ToLowerInvariant().Trim()) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }

        // After updating user and before returning Page()
        HttpContext.Session.SetString("FirstName", user.FirstName ?? "");
        HttpContext.Session.SetString("LastName", user.LastName ?? "");

        Message = "Account updated successfully.";
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSelectTeamAsync()
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrEmpty(userEmail))
            return RedirectToPage("/Login");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
            return RedirectToPage("/Login");

        if (SelectedTeamId == null)
        {
            TeamMessage = "Please select a team.";
            await OnGetAsync();
            return Page();
        }

        user.SelectedTeamId = SelectedTeamId.Value;
        _dbContext.Users.Update(user);
        var rows = await _dbContext.SaveChangesAsync();

        TeamMessage = rows > 0
            ? "Team selection saved successfully."
            : "Failed to save team selection.";

        await OnGetAsync();
        return Page();
    }
}