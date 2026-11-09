using CodeLearning.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace CodeLearning.Api.Middleware;

// The access token is read in one place (OnMessageReceived) and checked against the revocation list in
// one place (OnTokenValidated), so the blacklist check always sees the exact token that authenticated
// the request, whichever header or cookie it came from
internal static class JwtBearerEventHandlers
{
    public const string AccessTokenCookie = "access_token";

    public static JwtBearerEvents Create() => new()
    {
        OnMessageReceived = OnMessageReceived,
        OnTokenValidated = OnTokenValidatedAsync,
        OnChallenge = OnChallengeAsync
    };

    private static Task OnMessageReceived(MessageReceivedContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Token = authHeader["Bearer ".Length..].Trim();
            return Task.CompletedTask;
        }

        context.Token = context.Request.Cookies[AccessTokenCookie];
        return Task.CompletedTask;
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(jti))
        {
            context.Fail(new AccessTokenRevokedException("Token has no id"));
            return;
        }

        var tokenService = context.HttpContext.RequestServices.GetRequiredService<ITokenService>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(JwtBearerEventHandlers));

        bool revoked;
        try
        {
            revoked = await tokenService.IsAccessTokenRevokedAsync(jti);
        }
        catch (Exception ex)
        {
            // Fail closed: without the revocation list a logged-out token cannot be told apart
            logger.LogError(ex, "Could not check access token {Jti} against the revocation list", jti);
            context.Fail(new TokenRevocationCheckUnavailableException(ex));
            return;
        }

        if (revoked)
        {
            logger.LogWarning("Attempt to use revoked JWT token: {Jti}", jti);
            context.Fail(new AccessTokenRevokedException("Token has been revoked"));
        }
    }

    // Every 401 from the bearer handler is a ProblemDetails body, like the rest of the API's errors
    private static async Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var response = context.Response;
        if (response.HasStarted)
        {
            return;
        }

        var detail = context.AuthenticateFailure switch
        {
            AccessTokenRevokedException => "Token has been revoked.",
            TokenRevocationCheckUnavailableException => "The token could not be verified right now. Please try again.",
            SecurityTokenExpiredException => "The access token has expired.",
            not null => "The access token is invalid.",
            null => "Authentication is required to access this resource."
        };

        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
            ? "Bearer"
            : "Bearer error=\"invalid_token\"";

        var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = detail,
                Instance = context.Request.Path
            }
        });
    }
}

internal sealed class AccessTokenRevokedException(string message) : Exception(message);

internal sealed class TokenRevocationCheckUnavailableException(Exception inner)
    : Exception("The token revocation list is unavailable", inner);
