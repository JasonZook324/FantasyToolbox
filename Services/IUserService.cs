using System.Threading.Tasks;

public interface IUserService
{
    Task<User> GetUserByEmailAsync(string email);
    Task UpdateLastLoginAsync(User user);
    Task<bool> UserExistsAsync(string email);
    Task CreateUserAsync(User user);
    Task<string> GenerateVerificationCodeAsync(User user);
    Task<bool> VerifyEmailCodeAsync(string email, string verificationCode);
    Task SetEmailVerifiedAsync(User user);
}