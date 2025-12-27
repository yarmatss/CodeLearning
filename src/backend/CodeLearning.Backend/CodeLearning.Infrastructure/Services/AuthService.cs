using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace CodeLearning.Infrastructure.Services;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ITokenService tokenService,
    IConfiguration configuration) : IAuthService
{
    public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
    {
        var existingUser = await userManager.FindByEmailAsync(registerDto.Email);
        if (existingUser != null)
        {
            throw new InvalidOperationException("User with this email already exists");
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
            throw new InvalidOperationException($"Registration failed: {errors}");
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        return MapToAuthResponse(user, "Registration successful");
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
    {
        var user = await userManager.FindByEmailAsync(loginDto.Email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            throw result.IsLockedOut
                ? new UnauthorizedAccessException("Account locked due to multiple failed login attempts")
                : new UnauthorizedAccessException("Invalid email or password");
        }

        return MapToAuthResponse(user, "Login successful");
    }

    public async Task LogoutAsync(string refreshToken, Guid userId, string jti, TimeSpan accessTokenExpiration)
    {
        // Revoke refresh token
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await tokenService.RevokeRefreshTokenAsync(refreshToken, userId);
        }

        // Blacklist current access token
        if (!string.IsNullOrEmpty(jti))
        {
            await tokenService.RevokeAccessTokenAsync(jti, accessTokenExpiration);
        }

        await signInManager.SignOutAsync();
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, Guid userId)
    {
        var isValid = await tokenService.ValidateRefreshTokenAsync(refreshToken, userId);
        if (!isValid)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not found");
        }

        // Revoke old refresh token
        await tokenService.RevokeRefreshTokenAsync(refreshToken, userId);

        // Generate new tokens (will be stored by controller)
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
