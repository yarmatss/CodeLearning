using CodeLearning.Application.DTOs.Auth;

namespace CodeLearning.Application.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
    Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
    // Either argument may be missing: the refresh cookie or the access token can be gone or expired
    Task LogoutAsync(string? refreshToken, Guid? userId, string? jti, TimeSpan accessTokenExpiration);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken);
}
