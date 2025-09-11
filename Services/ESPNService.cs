using System.Threading.Tasks;
using FantasyToolbox.Models;
using Microsoft.EntityFrameworkCore;

public class ESPNService : IESPNService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogService _logger;
    public ESPNService(ApplicationDbContext dbContext, ILogService logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<User> GetUserByEmailAsync(string email)
    {
        return await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<EspnAuth> GetEspnAuthByUserIdAsync(int userId)
    {
        return await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == userId);
    }

    public async Task<FLeagueData> GetLeagueDataByUserIdAsync(int userId)
    {
        return await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == userId);
    }

    public async Task UpsertEspnAuthAsync(int userId, string swid, string espnS2)
    {
        var auth = await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == userId);
        if (auth == null)
        {
            auth = new EspnAuth
            {
                UserId = userId,
                Swid = swid,
                EspnS2 = espnS2
            };
            _dbContext.EspnAuth.Add(auth);
        }
        else
        {
            auth.Swid = swid;
            auth.EspnS2 = espnS2;
            _dbContext.EspnAuth.Update(auth);
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpsertLeagueDataAsync(int userId, string leagueId, int leagueYear)
    {
        var leagueData = await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == userId);
        if (leagueData == null)
        {
            leagueData = new FLeagueData
            {
                UserId = userId,
                LeagueId = leagueId,
                LeagueYear = leagueYear
            };
            _dbContext.FLeagueData.Add(leagueData);
        }
        else
        {
            leagueData.LeagueId = leagueId;
            leagueData.LeagueYear = leagueYear;
            _dbContext.FLeagueData.Update(leagueData);
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateEspnConnectedSessionAsync(HttpContext httpContext)
    {
        var userEmail = httpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        var user = await GetUserByEmailAsync(userEmail);
        if (user == null)
        {
            httpContext.Session.SetString("EspnConnected", "false");
            return;
        }

        try
        {
            var auth = await GetEspnAuthByUserIdAsync(user.UserId);
            var leagueData = await GetLeagueDataByUserIdAsync(user.UserId);

            bool hasAuth = auth != null && !string.IsNullOrEmpty(auth.Swid) && !string.IsNullOrEmpty(auth.EspnS2);
            bool hasLeague = leagueData != null && !string.IsNullOrEmpty(leagueData.LeagueId) && leagueData.LeagueYear != 0;
            bool isConnected = hasAuth && hasLeague;

            httpContext.Session.SetString("EspnConnected", isConnected ? "true" : "false");
        }
        catch (Exception ex)
        {
            _logger.LogAsync($"Failed to get auth/league data for {user.UserId}", "Error", ex.Message).GetAwaiter().GetResult();
        }
    }
}