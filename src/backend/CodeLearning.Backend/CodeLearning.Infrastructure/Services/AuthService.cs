using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Exceptions;
using CodeLearning.Application.Services;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CodeLearning.Infrastructure.Services;

public class AuthService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ITokenService tokenService,
    IOptions<JwtSettings> jwtOptions) : IAuthService
{
    // Same message for an unknown email, a wrong password and a locked account, so the response does
    // not reveal which accounts exist or are locked
    private const string InvalidCredentialsMessage = "Invalid email or password";

    // Hashed when the email is unknown, so that path takes about as long as a real password check
    private static readonly User TimingDummyUser = new() { FirstName = string.Empty, LastName = string.Empty };

    private TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpirationDays);
    private TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);

    public async Task<AuthSessionResult> RegisterAsync(RegisterDto registerDto)
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

        return await StartSessionAsync(user, "Registration successful");
    }

    public async Task<AuthSessionResult> LoginAsync(LoginDto loginDto)
    {
        var user = await userManager.FindByEmailAsync(loginDto.Email);
        if (user == null)
        {
            userManager.PasswordHasher.HashPassword(TimingDummyUser, loginDto.Password);
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        // Only checks the password and counts failures for lockout; the session itself is the JWT.
        // A locked account gets the same answer as a wrong password; the per-IP-and-email rate limit
        // keeps others from locking an account out cheaply
        var result = await signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            throw new AuthenticationFailedException(InvalidCredentialsMessage);
        }

        return await StartSessionAsync(user, "Login successful");
    }

    public async Task<AuthSessionResult> RefreshTokenAsync(string refreshToken)
    {
        var newRefreshToken = tokenService.GenerateRefreshToken();

        // The refresh token alone identifies the user; the (possibly expired) access token is not trusted
        var session = await tokenService.RotateRefreshTokenAsync(refreshToken, newRefreshToken, RefreshTokenLifetime)
            ?? throw new AuthenticationFailedException("Invalid or expired refresh token");

        var user = await userManager.FindByIdAsync(session.UserId.ToString());
        if (user == null)
        {
            await tokenService.RevokeRefreshTokenAsync(newRefreshToken);
            throw new AuthenticationFailedException("Invalid or expired refresh token");
        }

        return CreateSessionResult(user, session.SessionId, newRefreshToken, "Token refreshed successfully");
    }

    public async Task LogoutAsync(string? refreshToken, Guid? userId, Guid? sessionId, string? jti, DateTimeOffset? accessTokenExpiresAt)
    {
        // The refresh cookie identifies the session even after the access token has expired
        var revoked = string.IsNullOrEmpty(refreshToken)
            ? null
            : await tokenService.RevokeRefreshTokenAsync(refreshToken);

        // Without the cookie (or if it belonged to another session) the access token's sid names it.
        // Only this session ends; the user's other devices stay signed in
        if (userId.HasValue && sessionId.HasValue && revoked?.SessionId != sessionId)
        {
            await tokenService.RevokeSessionAsync(userId.Value, sessionId.Value);
        }

        if (!string.IsNullOrEmpty(jti))
        {
            var remaining = accessTokenExpiresAt.HasValue
                ? accessTokenExpiresAt.Value - DateTimeOffset.UtcNow
                : AccessTokenLifetime;

            if (remaining > TimeSpan.Zero)
            {
                await tokenService.RevokeAccessTokenAsync(jti, remaining);
            }
        }
    }

    public Task RevokeAllSessionsAsync(Guid userId) => tokenService.RevokeAllRefreshTokensAsync(userId);

    private async Task<AuthSessionResult> StartSessionAsync(User user, string message)
    {
        var sessionId = Guid.NewGuid();
        var refreshToken = tokenService.GenerateRefreshToken();

        await tokenService.StoreRefreshTokenAsync(refreshToken, new RefreshSession(user.Id, sessionId), RefreshTokenLifetime);

        return CreateSessionResult(user, sessionId, refreshToken, message);
    }

    private AuthSessionResult CreateSessionResult(User user, Guid sessionId, string refreshToken, string message)
    {
        var accessToken = tokenService.GenerateAccessToken(user, sessionId);

        return new AuthSessionResult(
            MapToAuthResponse(user, message),
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshToken,
            DateTimeOffset.UtcNow.Add(RefreshTokenLifetime));
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
