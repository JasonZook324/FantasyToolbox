using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using Newtonsoft.Json.Linq;

[Authorize]
public class AccountModel : PageModel
{
    private readonly IConfiguration _configuration;

    public AccountModel(IConfiguration configuration)
    {
        _configuration = configuration;
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

        // Get ESPN credentials and league info
        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        int? selectedTeamId = null;
        string swid = null, espn_s2 = null, leagueId = null;
        int seasonYear = 0;

        using (var cmd = new NpgsqlCommand("SELECT selected_team_id FROM users WHERE email = @email", conn))
        {
            cmd.Parameters.AddWithValue("email", userEmail);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value)
                selectedTeamId = (int)result;
        }
        SelectedTeamId = selectedTeamId;

        using (var cmd = new NpgsqlCommand(
            @"SELECT ea.swid, ea.espn_s2, fld.leagueid, fld.league_year
              FROM users u
              JOIN espn_auth ea ON u.userid = ea.userid
              JOIN f_league_data fld ON u.userid = fld.userid
              WHERE u.email = @email", conn))
        {
            cmd.Parameters.AddWithValue("email", userEmail);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                swid = reader["swid"]?.ToString();
                espn_s2 = reader["espn_s2"]?.ToString();
                leagueId = reader["leagueid"]?.ToString();
                seasonYear = reader["league_year"] != DBNull.Value && int.TryParse(reader["league_year"].ToString(), out var year) ? year : 0;
            }
        }

        // Get teams from ESPN API
        if (!string.IsNullOrEmpty(swid) && !string.IsNullOrEmpty(espn_s2) && !string.IsNullOrEmpty(leagueId) && seasonYear > 0)
        {
            try
            {
                using var handler = new HttpClientHandler { UseCookies = false };
                using var httpClient = new HttpClient(handler);
                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{seasonYear}/segments/0/leagues/{leagueId}?view=mTeam";
                httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={swid}; espn_s2={espn_s2}");
                var response = await httpClient.GetAsync(apiUrl);
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
                        Console.WriteLine("loc", teamName);
                        Teams.Add(new TeamInfo { Id = (int)team["id"], Name = teamName + " ("+abbv+")" } );
                    }
                }
                    if (response.IsSuccessStatusCode)
                {

                    //using var doc = JsonDocument.Parse(json);
                    //if (doc.RootElement.TryGetProperty("teams", out var teamsProp))
                    //{
                    //    foreach (var team in teamsProp.EnumerateArray())
                    //    {
                    //        var id = team.GetProperty("id").GetInt32();
                    //        var name = team.GetProperty("location").GetString() + " " + team.GetProperty("nickname").GetString();
                    //        Teams.Add(new TeamInfo { Id = id, Name = name });
                    //    }
                    //}
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
            await OnGetAsync(); // Repopulate teams
            return Page();
        }

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // Get current password hash
        using var getCmd = new NpgsqlCommand("SELECT passwordhash FROM users WHERE email = @email", conn);
        getCmd.Parameters.AddWithValue("email", userEmail);
        var dbPassword = (string)await getCmd.ExecuteScalarAsync();

        var hasher = new PasswordHasher<string>();
        var result = dbPassword != null ? hasher.VerifyHashedPassword(null, dbPassword, Input.CurrentPassword) : PasswordVerificationResult.Failed;

        if (result != PasswordVerificationResult.Success)
        {
            Message = "Current password is incorrect.";
            await OnGetAsync(); // Repopulate teams
            return Page();
        }

        // Update email and/or password
        string updateSql = "UPDATE users SET email = @newEmail";
        if (!string.IsNullOrEmpty(Input.NewPassword))
            updateSql += ", passwordhash = @newPasswordHash";
        updateSql += " WHERE email = @email";

        using var updateCmd = new NpgsqlCommand(updateSql, conn);
        updateCmd.Parameters.AddWithValue("newEmail", Input.NewEmail.ToLowerInvariant().Trim());
        updateCmd.Parameters.AddWithValue("email", userEmail);
        if (!string.IsNullOrEmpty(Input.NewPassword))
        {
            var newHash = hasher.HashPassword(null, Input.NewPassword);
            updateCmd.Parameters.AddWithValue("newPasswordHash", newHash);
        }
        await updateCmd.ExecuteNonQueryAsync();

        // Update authentication cookie if email changed
        if (Input.NewEmail.ToLowerInvariant().Trim() != userEmail.ToLowerInvariant().Trim())
        {
            var claims = new[] { new Claim(ClaimTypes.Name, Input.NewEmail.ToLowerInvariant().Trim()) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        }

        Message = "Account updated successfully.";
        await OnGetAsync(); // Repopulate teams
        return Page();
    }

    public async Task<IActionResult> OnPostSelectTeamAsync()
    {
        var userEmail = User.Identity?.Name;
        if (string.IsNullOrEmpty(userEmail))
            return RedirectToPage("/Login");

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        if (SelectedTeamId == null)
        {
            TeamMessage = "Please select a team.";
            await OnGetAsync();
            return Page();
        }

        using var updateCmd = new NpgsqlCommand("UPDATE users SET selected_team_id = @teamId WHERE email = @email", conn);
        updateCmd.Parameters.AddWithValue("teamId", SelectedTeamId.Value);
        updateCmd.Parameters.AddWithValue("email", userEmail);
        var rows = await updateCmd.ExecuteNonQueryAsync();

        if (rows > 0)
        {
            TeamMessage = "Team selection saved successfully.";
        }
        else
        {
            TeamMessage = "Failed to save team selection.";
        }

        await OnGetAsync();
        return Page();
    }
}