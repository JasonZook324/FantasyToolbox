public interface ILogService
{
    Task LogAsync(string message, string level = "Info", string? exception = null, int? userId = null);
}