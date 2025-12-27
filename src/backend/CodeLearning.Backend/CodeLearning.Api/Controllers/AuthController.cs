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
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
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
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
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
        var refreshToken = Request.Cookies["refresh_token"];
        
        // Extract JTI from current access token
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;
        
        // Get access token expiration time
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
        var refreshToken = Request.Cookies["refresh_token"];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token not found" });
        }

        // Extract userId from current access token (might be expired but we can still read claims)
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
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var firstName = User.FindFirst(ClaimTypes.GivenName)?.Value;
        var lastName = User.FindFirst(ClaimTypes.Surname)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        return Ok(new 
        { 
            userId, 
            email, 
            firstName, 
            lastName, 
            role 
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
        var accessTokenExpirationMinutes = int.Parse(jwtSettings["AccessTokenExpirationMinutes"] ?? "60");
        var refreshTokenExpirationDays = int.Parse(jwtSettings["RefreshTokenExpirationDays"] ?? "7");

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(accessTokenExpirationMinutes)
        };

        Response.Cookies.Append("access_token", accessToken, cookieOptions);

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenExpirationDays)
        };

        Response.Cookies.Append("refresh_token", refreshToken, refreshCookieOptions);
    }

    private void ClearTokenCookies()
    {
        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
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
