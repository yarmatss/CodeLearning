using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly IValidator<RegisterDto> _registerValidator;
    private readonly IValidator<LoginDto> _loginValidator;

    public AuthController(
        IAuthService authService,
        ITokenService tokenService,
        IValidator<RegisterDto> registerValidator,
        IValidator<LoginDto> loginValidator)
    {
        _authService = authService;
        _tokenService = tokenService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
    {
        var validationResult = await _registerValidator.ValidateAsync(registerDto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try {
            var response = await _authService.RegisterAsync(registerDto);
            
            var user = new User 
            { 
                Id = Guid.Parse(response.UserId),
                Email = response.Email,
                FirstName = response.FirstName,
                LastName = response.LastName,
                Role = Enum.Parse<UserRole>(response.Role)
            };
            
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        var validationResult = await _loginValidator.ValidateAsync(loginDto);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _authService.LoginAsync(loginDto);

            var user = new User 
            { 
                Id = Guid.Parse(response.UserId),
                Email = response.Email,
                FirstName = response.FirstName,
                LastName = response.LastName,
                Role = Enum.Parse<UserRole>(response.Role)
            };
            
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

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
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _authService.LogoutAsync(refreshToken);
        }

        Response.Cookies.Delete("access_token");
        Response.Cookies.Delete("refresh_token");

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

        try
        {
            var response = await _authService.RefreshTokenAsync(refreshToken);

            var accessToken = _tokenService.GenerateAccessToken(new User 
            { 
                Id = Guid.Parse(response.UserId),
                Email = response.Email,
                FirstName = response.FirstName,
                LastName = response.LastName,
                Role = Enum.Parse<UserRole>(response.Role)
            });
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            SetTokenCookies(accessToken, newRefreshToken);

            return Ok(new { message = "Token refreshed successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            Response.Cookies.Delete("access_token");
            Response.Cookies.Delete("refresh_token");
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var firstName = User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value;
        var lastName = User.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value;
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        return Ok(new 
        { 
            userId, 
            email, 
            firstName, 
            lastName, 
            role 
        });
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
}
