using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using FantasyToolbox.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to bind to all interfaces on port 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
});

// Configure forwarded headers for Replit proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Get connection string from environment variable for security (Replit provides DATABASE_URL)
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
                      Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                      builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string not found. Please set DB_CONNECTION_STRING environment variable or configure DefaultConnection in appsettings.json.");
}

// Add DbContext for Identity
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Add distributed memory cache for sessions
builder.Services.AddDistributedMemoryCache();

// Add Identity services - disable email confirmation for now
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Configure Identity cookie options to work with custom pages and harden security
builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/Login";
    options.LogoutPath = "/Logout";
    options.AccessDeniedPath = "/Login";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Configure session options for security
builder.Services.Configure<CookiePolicyOptions>(options => {
    options.Secure = CookieSecurePolicy.Always;
});

// Configure data protection for persistent keys
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "DataProtectionKeys")));

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSession();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IESPNService, ESPNService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IEmailService, EmailService>();
var app = builder.Build();

// Apply database migrations on startup
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    // Check if database is accessible
    if (context.Database.CanConnect())
    {
        // Apply pending migrations
        context.Database.Migrate();
        Console.WriteLine("Database migrations applied successfully.");
    }
    else
    {
        Console.WriteLine("Warning: Cannot connect to database. Skipping migrations.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Failed to apply database migrations: {ex.Message}");
    // Continue startup even if migrations fail (for development)
}

// Use forwarded headers for proxy support
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Only use HTTPS redirection in production - Replit handles this at proxy level
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
