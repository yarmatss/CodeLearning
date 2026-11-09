using CodeLearning.Api.Extensions;
using CodeLearning.Api.Middleware;
using CodeLearning.Application.DTOs.Auth;
using CodeLearning.Application.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CodeLearning.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    IValidator<RegisterDto> registerValidator,
    IValidator<LoginDto> loginValidator) : ControllerBase
{
    private const string AccessTokenCookie = JwtBearerEventHandlers.AccessTokenCookie;
    private const string RefreshTokenCookie = "refresh_token";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Register(RegisterDto registerDto)
    {
        await registerValidator.ValidateAndThrowAsync(registerDto);

        var session = await authService.RegisterAsync(registerDto);
        SetTokenCookies(session);

        return Ok(MapToLoginResponse(session));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.LoginPolicy)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginDto loginDto)
    {
        await loginValidator.ValidateAndThrowAsync(loginDto);

        var session = await authService.LoginAsync(loginDto);
        SetTokenCookies(session);

        return Ok(MapToLoginResponse(session));
    }

    // Anonymous so a client whose access token already expired can still end its session.
    // Ends only this session: the refresh cookie (or the access token's sid) names it
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await authService.LogoutAsync(
            Request.Cookies[RefreshTokenCookie],
            User.GetUserIdOrNull(),
            GetSessionId(User),
            User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value,
            GetAccessTokenExpiry(User));

        ClearTokenCookies();

        return Ok(new { message = "Logout successful" });
    }

    [HttpPost("refresh")]
    // Not rate limited: every signed-in client calls this every few minutes, and a 64-byte random
    // refresh token cannot be guessed, so throttling it would only lock out real users
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie];

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token not found" });
        }

        // The refresh token alone identifies the user and session; the (possibly expired) access token is not trusted
        var session = await authService.RefreshTokenAsync(refreshToken);
        SetTokenCookies(session);

        return Ok(new
        {
            message = "Token refreshed successfully",
            accessTokenExpiresAt = session.AccessTokenExpiresAt
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
            Role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
            AccessTokenExpiresAt = GetAccessTokenExpiry(User)
        });
    }

    private static LoginResponseDto MapToLoginResponse(AuthSessionResult session) => new()
    {
        UserId = session.User.UserId,
        Email = session.User.Email,
        FirstName = session.User.FirstName,
        LastName = session.User.LastName,
        Role = session.User.Role,
        Message = session.User.Message,
        AccessTokenExpiresAt = session.AccessTokenExpiresAt
    };

    // The inbound claim map may rename sid, so both spellings are accepted
    private static Guid? GetSessionId(ClaimsPrincipal user) =>
        Guid.TryParse((user.FindFirst(JwtRegisteredClaimNames.Sid) ?? user.FindFirst(ClaimTypes.Sid))?.Value, out var id)
            ? id
            : null;

    private static DateTimeOffset? GetAccessTokenExpiry(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : null;

    // Each cookie lives exactly as long as its token, so the browser drops an expired access token
    // instead of sending it for days after the server stopped accepting it
    private void SetTokenCookies(AuthSessionResult session)
    {
        var accessCookieOptions = CreateCookieOptions();
        accessCookieOptions.Expires = session.AccessTokenExpiresAt;

        var refreshCookieOptions = CreateCookieOptions();
        refreshCookieOptions.Expires = session.RefreshTokenExpiresAt;

        Response.Cookies.Append(AccessTokenCookie, session.AccessToken, accessCookieOptions);
        Response.Cookies.Append(RefreshTokenCookie, session.RefreshToken, refreshCookieOptions);
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
