using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using FantasyToolbox.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;

public static class EspnSessionHelper
{
    public static async Task UpdateEspnConnectedSessionAsync(HttpContext httpContext, IConfiguration configuration, ApplicationDbContext dbContext)
    {
        var userEmail = httpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        if (user == null)
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var auth = await dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        var leagueData = await dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == user.UserId);

        bool hasAuth = auth != null && !string.IsNullOrEmpty(auth.Swid) && !string.IsNullOrEmpty(auth.EspnS2);
        bool hasLeague = leagueData != null && !string.IsNullOrEmpty(leagueData.LeagueId) && leagueData.LeagueYear != 0;
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