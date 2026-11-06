using CodeLearning.Api.Extensions;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
    IOptions<JwtSettings> jwtOptions,
    IValidator<RegisterDto> registerValidator,
    IValidator<LoginDto> loginValidator) : ControllerBase
{
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterDto registerDto)
    {
        await registerValidator.ValidateAndThrowAsync(registerDto);

        var response = await authService.RegisterAsync(registerDto);
        var user = MapToUser(response);

        var (accessToken, refreshToken, accessTokenExpiresAt) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, refreshToken);

        return Ok(MapToLoginResponse(response, accessTokenExpiresAt));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        await loginValidator.ValidateAndThrowAsync(loginDto);

        var response = await authService.LoginAsync(loginDto);
        var user = MapToUser(response);

        var (accessToken, refreshToken, accessTokenExpiresAt) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, refreshToken);

        return Ok(MapToLoginResponse(response, accessTokenExpiresAt));
    }

    // Anonymous so a client whose access token already expired can still end its session
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        var userId = User.GetUserIdOrNull();
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var accessTokenExpiration = TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);

        await authService.LogoutAsync(Request.Cookies[RefreshTokenCookie], userId, jti, accessTokenExpiration);

        ClearTokenCookies();

        return Ok(new { message = "Logout successful" });
    }

    [HttpPost("refresh")]
    // Not in the auth rate limit: every signed-in client calls this every few minutes, and a 64-byte
    // random refresh token cannot be guessed, so throttling it would only lock out real users
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token not found" });
        }

        // The refresh token alone identifies the user; the (possibly expired) access token is not trusted
        var response = await authService.RefreshTokenAsync(refreshToken);
        var user = MapToUser(response);

        var (accessToken, newRefreshToken, accessTokenExpiresAt) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, newRefreshToken);

        return Ok(new
        {
            message = "Token refreshed successfully",
            accessTokenExpiresAt
        });
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    public IActionResult GetCurrentUser()
    {
        return Ok(new CurrentUserDto
        {
            UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            Email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
            FirstName = User.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty,
            LastName = User.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty,
            Role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty
        });
    }

    private static User MapToUser(AuthResponseDto response) => new()
    {
        Id = Guid.Parse(response.UserId),
        Email = response.Email,
        FirstName = response.FirstName,
        LastName = response.LastName,
        Role = Enum.Parse<UserRole>(response.Role)
    };

    private static LoginResponseDto MapToLoginResponse(AuthResponseDto response, DateTimeOffset accessTokenExpiresAt) => new()
    {
        UserId = response.UserId,
        Email = response.Email,
        FirstName = response.FirstName,
        LastName = response.LastName,
        Role = response.Role,
        Message = response.Message,
        AccessTokenExpiresAt = accessTokenExpiresAt
    };

    private async Task<(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt)> GenerateAndStoreTokensAsync(User user)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();

        var refreshTokenExpirationDays = jwtOptions.Value.RefreshTokenExpirationDays;

        await tokenService.StoreRefreshTokenAsync(
            refreshToken,
            user.Id,
            TimeSpan.FromDays(refreshTokenExpirationDays));

        // Read the expiry back from the token so it matches the exp claim exactly
        var accessTokenExpiresAt = new DateTimeOffset(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo, TimeSpan.Zero);

        return (accessToken, refreshToken, accessTokenExpiresAt);
    }

    private void SetTokenCookies(string accessToken, string refreshToken)
    {
        var cookieOptions = CreateCookieOptions();
        cookieOptions.Expires = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenExpirationDays);

        Response.Cookies.Append(AccessTokenCookie, accessToken, cookieOptions);
        Response.Cookies.Append(RefreshTokenCookie, refreshToken, cookieOptions);
    }

    // Deleting with the same attributes the cookies were set with, so browsers match and drop them
    private void ClearTokenCookies()
    {
        Response.Cookies.Delete(AccessTokenCookie, CreateCookieOptions());
        Response.Cookies.Delete(RefreshTokenCookie, CreateCookieOptions());
    }

    private static CookieOptions CreateCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict
    };
}
