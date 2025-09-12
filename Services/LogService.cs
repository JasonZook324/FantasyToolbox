using System.Numerics;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public class LogService : ILogService
{
    private readonly ApplicationDbContext _context;

    public LogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string message, string level = "Info", string? exception = null, int? userId = null)
    {
        try
        {
            // Only set userId if it's provided and exists in the database
            if (userId.HasValue && userId.Value > 0)
            {
                var userExists = await _context.Users.AnyAsync(u => u.UserId == userId.Value);
                if (!userExists)
                {
                    userId = null; // User doesn't exist, set to null
                }
            }

            var log = new AppLog
            {
                Message = message,
                Level = level,
                Exception = exception,
                UserId = userId // Will be null if user doesn't exist or wasn't specified
            };
            _context.AppLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log to console if database fails - don't crash the application
            Console.WriteLine($"[{DateTime.UtcNow}] {level}: {message}");
            if (!string.IsNullOrEmpty(exception))
            {
                Console.WriteLine($"Exception: {exception}");
            }
            Console.WriteLine($"Database logging failed: {ex.Message}");
            // Don't re-throw - let the application continue working even if database logging fails
        }
    }
}