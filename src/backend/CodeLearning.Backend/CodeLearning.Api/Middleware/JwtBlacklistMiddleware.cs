using CodeLearning.Application.Services;
using System.IdentityModel.Tokens.Jwt;

namespace CodeLearning.Api.Middleware;

public class JwtBlacklistMiddleware(RequestDelegate next, ILogger<JwtBlacklistMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITokenService tokenService)
    {
        var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        
        if (string.IsNullOrEmpty(token))
        {
            token = context.Request.Cookies["access_token"] ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                
                var jti = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
                
                if (!string.IsNullOrEmpty(jti))
                {
                    var isRevoked = await tokenService.IsAccessTokenRevokedAsync(jti);
                    
                    if (isRevoked)
                    {
                        logger.LogWarning("Attempt to use revoked JWT token: {Jti}", jti);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { message = "Token has been revoked" });
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error reading JWT token for blacklist check");
            }
        }

        await next(context);
    }
}
