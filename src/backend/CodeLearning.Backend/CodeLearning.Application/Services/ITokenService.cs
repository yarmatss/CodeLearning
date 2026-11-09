using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Services;

public interface ITokenService
{
    IssuedAccessToken GenerateAccessToken(User user, Guid sessionId);
    string GenerateRefreshToken();

    // Starts a new session; a user may have several at once (one per device or browser)
    Task StoreRefreshTokenAsync(string refreshToken, RefreshSession session, TimeSpan expiration);

    // Atomically invalidates the current token and stores its replacement for the same session.
    // Returns null when the current token is unknown, expired or already used
    Task<RefreshSession?> RotateRefreshTokenAsync(string currentRefreshToken, string newRefreshToken, TimeSpan expiration);

    // Revokes one session by its refresh token; returns the session it belonged to, if any
    Task<RefreshSession?> RevokeRefreshTokenAsync(string refreshToken);

    // Revokes one session by id, for a logout that has the access token but not the refresh cookie
    Task RevokeSessionAsync(Guid userId, Guid sessionId);

    // Revokes every session of the user, e.g. after a password or security-stamp change
    Task RevokeAllRefreshTokensAsync(Guid userId);

    Task RevokeAccessTokenAsync(string jti, TimeSpan expiration);
    Task<bool> IsAccessTokenRevokedAsync(string jti);
}
