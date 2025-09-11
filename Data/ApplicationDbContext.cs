using Microsoft.EntityFrameworkCore;
using FantasyToolbox.Models; // Update to your actual models namespace

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<EspnAuth> EspnAuth { get; set; }
    public DbSet<FLeagueData> FLeagueData { get; set; }
    public DbSet<AppLog> AppLogs { get; set; }
}