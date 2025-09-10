using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FantasyToolbox.Models;

public class ConnectESPNModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public ConnectESPNModel(ApplicationDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
    }

    [BindProperty]
    public InputModel Input { get; set; }

    public string SuccessMessage { get; set; }
    public string ErrorMessage { get; set; }
    public string LeagueName { get; set; }

    public class InputModel
    {
        [Required]
        [Display(Name = "SWID")]
        public string SWID { get; set; }

        [Required]
        [Display(Name = "espn_s2")]
        public string ESPN_S2 { get; set; }

        [Required]
        [Display(Name = "League ID")]
        public string LeagueId { get; set; }

        [Required]
        [Range(2000, 2100)]
        [Display(Name = "Season Year")]
        public int SeasonYear { get; set; }
    }

    public async Task OnGetAsync()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            ErrorMessage = "You must be logged in to connect your ESPN account.";
            return;
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
        {
            ErrorMessage = "User not found.";
            return;
        }

        Input = new InputModel();

        var auth = await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        if (auth != null)
        {
            Input.SWID = auth.Swid;
            Input.ESPN_S2 = auth.EspnS2;
        }

        var leagueData = await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == user.UserId);
        if (leagueData != null)
        {
            Input.LeagueId = leagueData.LeagueId;
            Input.SeasonYear = leagueData.LeagueYear;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please fill in all fields correctly.";
            return Page();
        }

        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            ErrorMessage = "You must be logged in to connect your ESPN account.";
            return Page();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
        {
            ErrorMessage = "User not found.";
            return Page();
        }

        // Upsert EspnAuth
        var auth = await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        if (auth == null)
        {
            auth = new EspnAuth
            {
                UserId = user.UserId,
                Swid = Input.SWID.Trim(),
                EspnS2 = Input.ESPN_S2.Trim()
            };
            _dbContext.EspnAuth.Add(auth);
        }
        else
        {
            auth.Swid = Input.SWID.Trim();
            auth.EspnS2 = Input.ESPN_S2.Trim();
            _dbContext.EspnAuth.Update(auth);
        }

        // Upsert FLeagueData
        var leagueData = await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == user.UserId);
        if (leagueData == null)
        {
            leagueData = new FLeagueData
            {
                UserId = user.UserId,
                LeagueId = Input.LeagueId.Trim(),
                LeagueYear = Input.SeasonYear
            };
            _dbContext.FLeagueData.Add(leagueData);
        }
        else
        {
            leagueData.LeagueId = Input.LeagueId.Trim();
            leagueData.LeagueYear = Input.SeasonYear;
            _dbContext.FLeagueData.Update(leagueData);
        }

        await _dbContext.SaveChangesAsync();


        // With this line:
        await EspnSessionHelper.UpdateEspnConnectedSessionAsync(HttpContext, _configuration, _dbContext);
        

        // ESPN API call for private league (requires cookies for authentication)
        try
        {
            using var handler = new HttpClientHandler();
            handler.UseCookies = false;

            using var httpClient = new HttpClient(handler);
            var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{Input.SeasonYear}/segments/0/leagues/{Input.LeagueId}?view=mSettings";
            httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={Input.SWID.Trim()}; espn_s2={Input.ESPN_S2.Trim()}");
            
            var response = await httpClient.GetAsync(apiUrl);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = $"Failed to connect to ESPN API. Status: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {json.Substring(0, Math.Min(json.Length, 300))}";
                return Page();
            }

            try
            {
                using var doc = JsonDocument.Parse(json);

                // Extract league name from settings.name
                if (doc.RootElement.TryGetProperty("settings", out var settingsProp) &&
                    settingsProp.TryGetProperty("name", out var leagueNameProp))
                {
                    LeagueName = leagueNameProp.GetString();
                    SuccessMessage = $"ESPN account connected successfully! League Name: {LeagueName}";
                }
                else
                {
                    ErrorMessage = $"Could not retrieve league name from ESPN API. Raw response: {json.Substring(0, Math.Min(json.Length, 500))}\nFull response:\n{json}";
                    return Page();
                }
            }
            catch (JsonException)
            {
                ErrorMessage = $"ESPN API did not return JSON. Raw response: {json.Substring(0, Math.Min(json.Length, 500))}\nFull response:\n{json}";
                return Page();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An error occurred while connecting to the ESPN API: {ex.Message}";
            return Page();
        }

        return Page();
    }
}