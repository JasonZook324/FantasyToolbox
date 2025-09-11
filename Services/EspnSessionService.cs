using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

public class EspnSessionService : IEspnSessionService
{
    private readonly IESPNService _espnService;

    public EspnSessionService(IESPNService espnService)
    {
        _espnService = espnService;
    }

    public async Task UpdateEspnConnectedSessionAsync(HttpContext httpContext)
    {
        var userEmail = httpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var user = await _espnService.GetUserByEmailAsync(userEmail);
        if (user == null)
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var auth = await _espnService.GetEspnAuthByUserIdAsync(user.UserId);
        var leagueData = await _espnService.GetLeagueDataByUserIdAsync(user.UserId);

        bool hasAuth = auth != null && !string.IsNullOrEmpty(auth.Swid) && !string.IsNullOrEmpty(auth.EspnS2);
        bool hasLeague = leagueData != null && !string.IsNullOrEmpty(leagueData.LeagueId) && leagueData.LeagueYear != 0;
        bool isConnected = hasAuth && hasLeague;

        httpContext.Session.SetString("EspnConnected", isConnected ? "true" : "false");
    }

    public void SetEspnBannerIfNeeded(HttpContext httpContext, IDictionary<string, object> viewData)
    {
        var espnConnected = httpContext.Session.GetString("EspnConnected");
        if (string.IsNullOrEmpty(espnConnected) || espnConnected != "true")
        {
            viewData["ShowConnectESPNBanner"] = true;
        }
    }
}