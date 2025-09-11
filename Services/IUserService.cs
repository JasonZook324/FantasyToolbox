using System.Threading.Tasks;

public interface IUserService
{
    Task<User> GetUserByEmailAsync(string email);
    Task UpdateLastLoginAsync(User user);
}