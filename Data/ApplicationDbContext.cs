using Microsoft.EntityFrameworkCore;
using FantasyToolbox.Models; // Update to your actual models namespace

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<EspnAuth> EspnAuth { get; set; }
    public DbSet<FLeagueData> FLeagueData { get; set; }
    public DbSet<AppLog> AppLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Force public schema and explicit table mapping
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.Entity<User>().ToTable("Users", "public");
        modelBuilder.Entity<EspnAuth>().ToTable("EspnAuth", "public");
        modelBuilder.Entity<FLeagueData>().ToTable("FLeagueData", "public");
        modelBuilder.Entity<AppLog>().ToTable("AppLogs", "public");

        base.OnModelCreating(modelBuilder);
    }
}