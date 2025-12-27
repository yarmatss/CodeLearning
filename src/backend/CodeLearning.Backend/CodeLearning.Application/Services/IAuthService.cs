using CodeLearning.Application.DTOs.Auth;

namespace CodeLearning.Application.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto);
    Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
    Task LogoutAsync(string refreshToken, Guid userId, string jti, TimeSpan accessTokenExpiration);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, Guid userId);
}
