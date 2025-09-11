using System.Numerics;
using System.Threading.Tasks;

public class LogService : ILogService
{
    private readonly ApplicationDbContext _context;

    public LogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string message, string level = "Info", string? exception = null, int userId = 2)
    {
        userId = 2; // Temporary hardcoded userId for testing
        var log = new AppLog
        {
            Message = message,
            Level = level,
            Exception = exception,
            UserId = userId
        };
        _context.AppLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}