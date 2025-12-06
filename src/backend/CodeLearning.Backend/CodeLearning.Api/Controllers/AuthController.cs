using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
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
        
        var (accessToken, refreshToken) = GenerateTokens(user);
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

        var (accessToken, refreshToken) = GenerateTokens(user);
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
        var refreshToken = Request.Cookies["refresh_token"];
        
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await authService.LogoutAsync(refreshToken);
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

        var response = await authService.RefreshTokenAsync(refreshToken);
        var user = MapToUser(response);

        var (accessToken, newRefreshToken) = GenerateTokens(user);
        SetTokenCookies(accessToken, newRefreshToken);

        return Ok(new { message = "Token refreshed successfully" });
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

    private (string AccessToken, string RefreshToken) GenerateTokens(User user)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        return (accessToken, refreshToken);
    }

    private void SetTokenCookies(string accessToken, string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(1)
        };

        Response.Cookies.Append("access_token", accessToken, cookieOptions);

        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("refresh_token", refreshToken, refreshCookieOptions);
    }

    private void ClearTokenCookies()
    {
        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");
    }
}
