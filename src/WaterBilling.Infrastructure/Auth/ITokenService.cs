using WaterBilling.Domain.Users;

namespace WaterBilling.Infrastructure.Auth;

public interface ITokenService
{
    AccessToken Issue(User user);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc, string TokenType = "Bearer");
