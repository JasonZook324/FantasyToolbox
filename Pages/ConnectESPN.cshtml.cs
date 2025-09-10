using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
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

        // Get user id from users table
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

        // Get ESPN Auth cookies
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

        // Get League Data
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

        // Get user id from users table
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

        // Save ESPN Auth cookies
        using (var insertAuthCmd = new NpgsqlCommand(
            "INSERT INTO espn_auth (userid, swid, espn_s2) VALUES (@userid, @swid, @espn_s2) " +
            "ON CONFLICT (userid) DO UPDATE SET swid = @swid, espn_s2 = @espn_s2", conn))
        {
            insertAuthCmd.Parameters.AddWithValue("userid", userId);
            insertAuthCmd.Parameters.AddWithValue("swid", Input.SWID.Trim());
            insertAuthCmd.Parameters.AddWithValue("espn_s2", Input.ESPN_S2.Trim());
            await insertAuthCmd.ExecuteNonQueryAsync();
        }

        // Save League Data
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
        EspnSessionHelper.SetEspnBannerIfNeeded(HttpContext, ViewData);

        SuccessMessage = "ESPN account connected successfully!";
        return Page();
    }
}