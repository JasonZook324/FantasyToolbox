using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using FantasyToolbox.Models; // Ensure this matches your actual models namespace

//DbContext
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<EspnAuth> EspnAuth { get; set; }
    public DbSet<FLeagueData> FLeagueData { get; set; }
}

public class AppPageModel : PageModel
{
    private readonly IEspnSessionService _espnSessionService;
    //private readonly ApplicationDbContext _dbContext;

    public AppPageModel(IEspnSessionService espnSessionService)
    {
        //_dbContext = dbContext;
        _espnSessionService = espnSessionService;
    }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        UpdateEspnConnectedSessionAsync().GetAwaiter().GetResult();
        base.OnPageHandlerExecuting(context);
    }

    protected async Task UpdateEspnConnectedSessionAsync()
    {
        await _espnSessionService.UpdateEspnConnectedSessionAsync(HttpContext);
        //var userEmail = HttpContext.Session.GetString("UserEmail");
        //if (string.IsNullOrEmpty(userEmail))
        //{
        //    HttpContext.Session.SetString("EspnConnected", "false");
        //    return;
        //}

        //// Get user id from Users table
        //var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
        //if (user == null)
        //{
        //    HttpContext.Session.SetString("EspnConnected", "false");
        //    return;
        //}
        //int userId = user.UserId;

        //// Get ESPN Auth cookies
        //var auth = await _dbContext.EspnAuth.FirstOrDefaultAsync(a => a.UserId == userId);
        //string swid = auth?.Swid;
        //string espn_s2 = auth?.EspnS2;

        //// Get League Data
        //var leagueData = await _dbContext.FLeagueData.FirstOrDefaultAsync(l => l.UserId == userId);
        //string leagueid = leagueData?.LeagueId;
        //int league_year = leagueData?.LeagueYear ?? 0;

        //bool hasAuth = !string.IsNullOrEmpty(swid) && !string.IsNullOrEmpty(espn_s2);
        //bool hasLeague = !string.IsNullOrEmpty(leagueid) && league_year != 0;
        //bool isConnected = hasAuth && hasLeague;

        //HttpContext.Session.SetString("EspnConnected", isConnected ? "true" : "false");
    }

    public static explicit operator AppPageModel(ConnectESPNModel v)
    {
        throw new NotImplementedException();
    }
}