using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;

public static class EspnSessionHelper
{
    public static void UpdateEspnConnectedSession(HttpContext httpContext, IConfiguration configuration)
    {
        var userEmail = httpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var connString = configuration.GetConnectionString("DefaultConnection");
        using var conn = new NpgsqlConnection(connString);
        conn.Open();

        int userId;
        using (var getUserCmd = new NpgsqlCommand("SELECT userid FROM users WHERE email = @email", conn))
        {
            getUserCmd.Parameters.AddWithValue("email", userEmail);
            var result = getUserCmd.ExecuteScalar();
            if (result == null)
            {
                httpContext.Session.SetString("EspnConnected", "false");
                return;
            }
            userId = (int)result;
        }

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

        httpContext.Session.SetString("EspnConnected", isConnected ? "true" : "false");
    }

    public static void SetEspnBannerIfNeeded(HttpContext httpContext, IDictionary<string, object> viewData)
    {
        var espnConnected = httpContext.Session.GetString("EspnConnected");
        if (string.IsNullOrEmpty(espnConnected) || espnConnected != "true")
        {
            viewData["ShowConnectESPNBanner"] = true;
        }
    }
}