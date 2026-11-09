using CodeLearning.Application.DTOs.Auth;

namespace CodeLearning.Application.Services;

public interface IAuthService
{
    Task<AuthSessionResult> RegisterAsync(RegisterDto registerDto);
    Task<AuthSessionResult> LoginAsync(LoginDto loginDto);
    Task<AuthSessionResult> RefreshTokenAsync(string refreshToken);

    // Ends only the caller's session. Any argument may be missing: the refresh cookie or the access
    // token can be gone or expired
    Task LogoutAsync(string? refreshToken, Guid? userId, Guid? sessionId, string? jti, DateTimeOffset? accessTokenExpiresAt);

    // Signs the user out everywhere; call after a password or security-stamp change
    Task RevokeAllSessionsAsync(Guid userId);
}
