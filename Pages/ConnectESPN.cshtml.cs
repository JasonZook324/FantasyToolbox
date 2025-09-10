using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;

public class ConnectESPNModel : PageModel
{
    private readonly IConfiguration _configuration;

    public ConnectESPNModel(IConfiguration configuration)
    {
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

    public void OnGet()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            ErrorMessage = "You must be logged in to connect your ESPN account.";
            return;
        }

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        conn.Open();

        int userId;
        using (var getUserCmd = new NpgsqlCommand("SELECT userid FROM users WHERE email = @email", conn))
        {
            getUserCmd.Parameters.AddWithValue("email", userEmail);
            var result = getUserCmd.ExecuteScalar();
            if (result == null)
            {
                ErrorMessage = "User not found.";
                return;
            }
            userId = (int)result;
        }

        Input = new InputModel();

        using (var getAuthCmd = new NpgsqlCommand(
            "SELECT swid, espn_s2 FROM espn_auth WHERE userid = @userid", conn))
        {
            getAuthCmd.Parameters.AddWithValue("userid", userId);
            using var reader = getAuthCmd.ExecuteReader();
            if (reader.Read())
            {
                Input.SWID = reader["swid"]?.ToString();
                Input.ESPN_S2 = reader["espn_s2"]?.ToString();
            }
            reader.Close();
        }

        using (var getLeagueCmd = new NpgsqlCommand(
            "SELECT leagueid, league_year FROM f_league_data WHERE userid = @userid", conn))
        {
            getLeagueCmd.Parameters.AddWithValue("userid", userId);
            using var reader = getLeagueCmd.ExecuteReader();
            if (reader.Read())
            {
                Input.LeagueId = reader["leagueid"]?.ToString();
                Input.SeasonYear = reader["league_year"] != DBNull.Value ? Convert.ToInt32(reader["league_year"]) : 0;
            }
            reader.Close();
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

        var connString = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        int userId;
        using (var getUserCmd = new NpgsqlCommand("SELECT userid FROM users WHERE email = @email", conn))
        {
            getUserCmd.Parameters.AddWithValue("email", userEmail);
            var result = await getUserCmd.ExecuteScalarAsync();
            if (result == null)
            {
                ErrorMessage = "User not found.";
                return Page();
            }
            userId = (int)result;
        }

        using (var insertAuthCmd = new NpgsqlCommand(
            "INSERT INTO espn_auth (userid, swid, espn_s2) VALUES (@userid, @swid, @espn_s2) " +
            "ON CONFLICT (userid) DO UPDATE SET swid = @swid, espn_s2 = @espn_s2", conn))
        {
            insertAuthCmd.Parameters.AddWithValue("userid", userId);
            insertAuthCmd.Parameters.AddWithValue("swid", Input.SWID.Trim());
            insertAuthCmd.Parameters.AddWithValue("espn_s2", Input.ESPN_S2.Trim());
            await insertAuthCmd.ExecuteNonQueryAsync();
        }

        using (var insertLeagueCmd = new NpgsqlCommand(
            "INSERT INTO f_league_data (userid, leagueid, league_year) VALUES (@userid, @leagueid, @league_year) " +
            "ON CONFLICT (userid) DO UPDATE SET leagueid = @leagueid, league_year = @league_year", conn))
        {
            insertLeagueCmd.Parameters.AddWithValue("userid", userId);
            insertLeagueCmd.Parameters.AddWithValue("leagueid", Input.LeagueId.Trim());
            insertLeagueCmd.Parameters.AddWithValue("league_year", Input.SeasonYear);
            await insertLeagueCmd.ExecuteNonQueryAsync();
        }

        EspnSessionHelper.UpdateEspnConnectedSession(HttpContext, _configuration);

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