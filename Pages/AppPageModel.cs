using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;

public class AppPageModel : PageModel
{
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        UpdateEspnConnectedSession();
        base.OnPageHandlerExecuting(context);
    }

    protected void UpdateEspnConnectedSession()
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            HttpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var configuration = HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
        var connString = configuration.GetConnectionString("DefaultConnection");
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
                HttpContext.Session.SetString("EspnConnected", "false");
                return;
            }
            userId = (int)result;
        }

        // Get ESPN Auth cookies
        string swid = null, espn_s2 = null;
        using (var getAuthCmd = new NpgsqlCommand("SELECT swid, espn_s2 FROM espn_auth WHERE userid = @userid", conn))
        {
            getAuthCmd.Parameters.AddWithValue("userid", userId);
            using var reader = getAuthCmd.ExecuteReader();
            if (reader.Read())
            {
                swid = reader["swid"]?.ToString();
                espn_s2 = reader["espn_s2"]?.ToString();
            }
            reader.Close();
        }

        // Get League Data
        string leagueid = null;
        int league_year = 0;
        using (var getLeagueCmd = new NpgsqlCommand("SELECT leagueid, league_year FROM f_league_data WHERE userid = @userid", conn))
        {
            getLeagueCmd.Parameters.AddWithValue("userid", userId);
            using var reader = getLeagueCmd.ExecuteReader();
            if (reader.Read())
            {
                leagueid = reader["leagueid"]?.ToString();
                league_year = reader["league_year"] != DBNull.Value ? Convert.ToInt32(reader["league_year"]) : 0;
            }
            reader.Close();
        }

        bool hasAuth = !string.IsNullOrEmpty(swid) && !string.IsNullOrEmpty(espn_s2);
        bool hasLeague = !string.IsNullOrEmpty(leagueid) && league_year != 0;
        bool isConnected = hasAuth && hasLeague;

        HttpContext.Session.SetString("EspnConnected", isConnected ? "true" : "false");
    }

    public static explicit operator AppPageModel(ConnectESPNModel v)
    {
        throw new NotImplementedException();
    }
}