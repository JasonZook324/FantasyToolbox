using System.Threading.Tasks;
using FantasyToolbox.Models;

public interface IESPNService
{
    Task<User> GetUserByEmailAsync(string email);
    Task<EspnAuth> GetEspnAuthByUserIdAsync(int userId);
    Task<FLeagueData> GetLeagueDataByUserIdAsync(int userId);
    Task UpsertEspnAuthAsync(int userId, string swid, string espnS2);
    Task UpsertLeagueDataAsync(int userId, string leagueId, int leagueYear);
    Task UpdateEspnConnectedSessionAsync(HttpContext httpContext);
}