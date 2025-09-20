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

// Get connection string from environment variable for security 
// Check for external database first, then fallback to Replit's managed database
var externalDatabaseUrl = Environment.GetEnvironmentVariable("EXTERNAL_DATABASE_URL");
var replitDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

string connectionString;
string databaseUrl = null;
string databaseSource = "Unknown";

// Try external database first if provided
if (!string.IsNullOrEmpty(externalDatabaseUrl))
{
    try
    {
        // Test if external URL can be parsed - handle special characters in query string
        string testUrl = externalDatabaseUrl;
        
        // Handle potential URI parsing issues with channel_binding parameter
        if (testUrl.Contains("channel_binding=require"))
        {
            // URL encode the query string part if needed
            var parts = testUrl.Split('?');
            if (parts.Length > 1)
            {
                var queryString = parts[1];
                // Replace problematic characters that might cause URI parsing issues
                queryString = queryString.Replace("&channel_binding=require", "");
                testUrl = parts[0] + "?" + queryString;
                Console.WriteLine($"Cleaned external database URL for parsing: removing channel_binding parameter");
            }
        }
        
        var testUri = new Uri(testUrl);
        databaseUrl = externalDatabaseUrl; // Use original URL for connection
        databaseSource = "External Neon Database";
        Console.WriteLine($"External database URL format is valid, using external database");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"External database URL format invalid ({ex.Message}), falling back to Replit managed database");
        databaseUrl = replitDatabaseUrl;
        databaseSource = "Replit Managed Database";
    }
}
else
{
    databaseUrl = replitDatabaseUrl;
    databaseSource = "Replit Managed Database";
}

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Parse DATABASE_URL format and convert to Npgsql connection string format
    // DATABASE_URL format: postgresql://user:password@host:port/database?sslmode=require
    try
    {
        var uri = new Uri(databaseUrl);
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432; // Default PostgreSQL port if not specified
        var database = uri.AbsolutePath.TrimStart('/');
        var userInfo = uri.UserInfo.Split(':');
        var username = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        
        // Extract SSL mode from query string
        var sslMode = "require"; // default
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            sslMode = queryParams["sslmode"] ?? "require";
        }
        
        // Build proper Npgsql connection string with connection pooling, timeout settings, and force public schema
        connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode={sslMode};Search Path=public;Connection Idle Lifetime=30;Maximum Pool Size=10;Minimum Pool Size=1;Timeout=15;Command Timeout=30;";
        
        Console.WriteLine($"Using {databaseSource} - Parsed connection string: Host={host}, Port={port}, Database={database}, Username={username}, SSL Mode={sslMode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error parsing database URL: {ex.Message}");
        throw new InvalidOperationException($"Invalid database URL format: {ex.Message}");
    }
}
else
{
    connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ??
                      builder.Configuration.GetConnectionString("DefaultConnection");
    
    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException("Database connection string not found. Please set DATABASE_URL environment variable or configure DefaultConnection in appsettings.json.");
    }
}

// Add DbContext for Identity with retry logic for transient failures
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
    }));

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

// Configure data protection for persistent keys with robust error handling
try 
{
    var keysPath = Path.Combine(Directory.GetCurrentDirectory(), "DataProtectionKeys");
    var keysDir = new DirectoryInfo(keysPath);
    if (!keysDir.Exists) keysDir.Create();
    
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keysDir)
        .SetApplicationName("FantasyToolbox")
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90)); // Keys last 90 days
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not configure persistent data protection keys: {ex.Message}");
    // Fallback to ephemeral keys if filesystem fails
    builder.Services.AddDataProtection()
        .SetApplicationName("FantasyToolbox");
}

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers(); // Add API controllers support
builder.Services.AddSession();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IESPNService, ESPNService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddScoped<GeminiService>();
var app = builder.Build();

// Apply database migrations on startup
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    // Check if database is accessible and apply migrations
    if (context.Database.CanConnect())
    {
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

app.MapControllers(); // Map API controllers
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
