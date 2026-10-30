using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Application.Settings;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = GetCurrentUserId();
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;

        var accessTokenExpiration = TimeSpan.FromMinutes(jwtOptions.Value.AccessTokenExpirationMinutes);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, userId, jti, accessTokenExpiration);
        }

        ClearTokenCookies();

        return Ok(new { message = "Logout successful" });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token not found" });
        }

        var userId = GetUserIdFromExpiredToken();
        if (userId == Guid.Empty)
        {
            return Unauthorized(new { message = "Invalid token" });
        }

        var response = await authService.RefreshTokenAsync(refreshToken, userId);
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
        var refreshTokenExpirationDays = jwtOptions.Value.RefreshTokenExpirationDays;

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenExpirationDays)
        };

        Response.Cookies.Append(AccessTokenCookie, accessToken, cookieOptions);
        Response.Cookies.Append(RefreshTokenCookie, refreshToken, cookieOptions);
    }

    private void ClearTokenCookies()
    {
        Response.Cookies.Delete(AccessTokenCookie);
        Response.Cookies.Delete(RefreshTokenCookie);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private Guid GetUserIdFromExpiredToken()
    {
        try
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            if (string.IsNullOrEmpty(token))
            {
                token = Request.Cookies["access_token"] ?? string.Empty;
            }

            if (string.IsNullOrEmpty(token))
                return Guid.Empty;

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier
                || c.Type == JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(userIdClaim?.Value, out var userId) ? userId : Guid.Empty;
        }
        catch
        {
            return Guid.Empty;
        }
    }
}
