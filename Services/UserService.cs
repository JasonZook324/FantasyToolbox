using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Security.Cryptography;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _dbContext;

    public UserService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User> GetUserByEmailAsync(string email)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant().Trim());
    }

    public async Task UpdateLastLoginAsync(User user)
    {
        user.LastLogin = DateTime.UtcNow;
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> UserExistsAsync(string email)
    {
        return await _dbContext.Users.AnyAsync(u => u.Email == email);
    }

    public async Task CreateUserAsync(User user)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<string> GenerateVerificationCodeAsync(User user)
    {
        // Generate a 6-digit verification code
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var code = (Math.Abs(BitConverter.ToInt32(bytes, 0)) % 900000 + 100000).ToString();
        
        // Set verification code and expiration (15 minutes)
        user.VerificationCode = code;
        user.VerificationCodeExpires = DateTime.UtcNow.AddMinutes(15);
        
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
        
        return code;
    }

    public async Task<bool> VerifyEmailCodeAsync(string email, string verificationCode)
    {
        var user = await GetUserByEmailAsync(email);
        
        if (user == null || 
            user.VerificationCode != verificationCode || 
            user.VerificationCodeExpires == null || 
            user.VerificationCodeExpires < DateTime.UtcNow)
        {
            return false;
        }
        
        return true;
    }

    public async Task SetEmailVerifiedAsync(User user)
    {
        user.EmailVerified = true;
        user.VerificationCode = null;
        user.VerificationCodeExpires = null;
        
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }
}