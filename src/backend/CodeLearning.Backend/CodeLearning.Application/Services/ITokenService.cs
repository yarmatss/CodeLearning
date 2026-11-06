using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();

    // Replaces the user's previous refresh token: one active session per user
    Task StoreRefreshTokenAsync(string refreshToken, Guid userId, TimeSpan expiration);

    // Single use: returns the owner and invalidates the token, or null when it is unknown or expired
    Task<Guid?> ConsumeRefreshTokenAsync(string refreshToken);

    Task RevokeRefreshTokenAsync(Guid userId);

    Task RevokeAccessTokenAsync(string jti, TimeSpan expiration);
    Task<bool> IsAccessTokenRevokedAsync(string jti);
}
