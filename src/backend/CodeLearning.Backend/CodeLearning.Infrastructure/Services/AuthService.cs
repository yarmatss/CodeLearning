using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using Microsoft.AspNetCore.Identity;

namespace CodeLearning.Infrastructure.Services;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ITokenService tokenService) : IAuthService
{
    public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
    {
        var existingUser = await userManager.FindByEmailAsync(registerDto.Email);
        if (existingUser != null)
        {
            throw new BusinessRuleException("User with this email already exists");
        }

        var user = new User
        {
            UserName = registerDto.Email,
            Email = registerDto.Email,
            FirstName = registerDto.FirstName,
            LastName = registerDto.LastName,
            Role = registerDto.Role == "Teacher" ? UserRole.Teacher : UserRole.Student,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(user, registerDto.Password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BusinessRuleException($"Registration failed: {errors}");
        }

        return MapToAuthResponse(user, "Registration successful");
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
    {
        var user = await userManager.FindByEmailAsync(loginDto.Email);
        if (user == null)
        {
            throw new AuthenticationFailedException("Invalid email or password");
        }

        // Only checks the password and counts failures for lockout; the session itself is the JWT issued by the controller
        var result = await signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            throw result.IsLockedOut
                ? new AuthenticationFailedException("Account locked due to multiple failed login attempts")
                : new AuthenticationFailedException("Invalid email or password");
        }

        return MapToAuthResponse(user, "Login successful");
    }

    public async Task LogoutAsync(string? refreshToken, Guid? userId, string? jti, TimeSpan accessTokenExpiration)
    {
        // The refresh cookie identifies the session even after the access token has expired
        var refreshTokenOwner = string.IsNullOrEmpty(refreshToken)
            ? null
            : await tokenService.ConsumeRefreshTokenAsync(refreshToken);

        var owner = userId ?? refreshTokenOwner;
        if (owner.HasValue)
        {
            await tokenService.RevokeRefreshTokenAsync(owner.Value);
        }

        if (!string.IsNullOrEmpty(jti))
        {
            await tokenService.RevokeAccessTokenAsync(jti, accessTokenExpiration);
        }
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
    {
        // Consuming the token also revokes it; the controller stores the replacement
        var userId = await tokenService.ConsumeRefreshTokenAsync(refreshToken)
            ?? throw new AuthenticationFailedException("Invalid or expired refresh token");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new AuthenticationFailedException("User not found");

        return MapToAuthResponse(user, "Token refreshed successfully");
    }

    private static AuthResponseDto MapToAuthResponse(User user, string message) => new()
    {
        UserId = user.Id.ToString(),
        Email = user.Email ?? string.Empty,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Role = user.Role.ToString(),
        Message = message
    };
}
