using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    
    Task<bool> ValidateRefreshTokenAsync(string refreshToken, Guid userId);
    Task StoreRefreshTokenAsync(string refreshToken, Guid userId, TimeSpan expiration);
    Task RevokeRefreshTokenAsync(string refreshToken, Guid userId);
    
    Task RevokeAccessTokenAsync(string jti, TimeSpan expiration);
    Task<bool> IsAccessTokenRevokedAsync(string jti);
}
