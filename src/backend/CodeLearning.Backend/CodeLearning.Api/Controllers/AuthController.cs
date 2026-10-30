using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
    IConfiguration configuration,
    IValidator<RegisterDto> registerValidator,
    IValidator<LoginDto> loginValidator) : ControllerBase
{
    private const string AccessTokenCookie = "access_token";
    private const string RefreshTokenCookie = "refresh_token";

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterDto registerDto)
    {
        await registerValidator.ValidateAndThrowAsync(registerDto);

        var response = await authService.RegisterAsync(registerDto);
        var user = MapToUser(response);

        var (accessToken, refreshToken) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, refreshToken);

        return Ok(new
        {
            response.UserId,
            response.Email,
            response.FirstName,
            response.LastName,
            response.Role,
            response.Message,
            accessToken,
            refreshToken
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        await loginValidator.ValidateAndThrowAsync(loginDto);

        var response = await authService.LoginAsync(loginDto);
        var user = MapToUser(response);

        var (accessToken, refreshToken) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, refreshToken);

        return Ok(new
        {
            response.UserId,
            response.Email,
            response.FirstName,
            response.LastName,
            response.Role,
            response.Message,
            accessToken,
            refreshToken
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = GetCurrentUserId();
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;

        var jwtSettings = configuration.GetSection("JwtSettings");
        var accessTokenExpirationMinutes = int.Parse(jwtSettings["AccessTokenExpirationMinutes"] ?? "60");
        var accessTokenExpiration = TimeSpan.FromMinutes(accessTokenExpirationMinutes);

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

        var (accessToken, newRefreshToken) = await GenerateAndStoreTokensAsync(user);
        SetTokenCookies(accessToken, newRefreshToken);

        return Ok(new
        {
            message = "Token refreshed successfully",
            accessToken,
            refreshToken = newRefreshToken
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

    private async Task<(string AccessToken, string RefreshToken)> GenerateAndStoreTokensAsync(User user)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();

        var jwtSettings = configuration.GetSection("JwtSettings");
        var refreshTokenExpirationDays = int.Parse(jwtSettings["RefreshTokenExpirationDays"] ?? "7");

        await tokenService.StoreRefreshTokenAsync(
            refreshToken,
            user.Id,
            TimeSpan.FromDays(refreshTokenExpirationDays));

        return (accessToken, refreshToken);
    }

    private void SetTokenCookies(string accessToken, string refreshToken)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var refreshTokenExpirationDays = int.Parse(jwtSettings["RefreshTokenExpirationDays"] ?? "7");

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
