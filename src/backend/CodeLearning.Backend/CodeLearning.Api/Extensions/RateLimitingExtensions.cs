using CodeLearning.Api.Middleware;
using CodeLearning.Application.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace CodeLearning.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string SubmissionsPolicy = "submissions";
    public const string AuthPolicy = "auth";
    public const string LoginPolicy = "login";

    extension(IServiceCollection services)
    {
        public IServiceCollection AddApiRateLimiting()
        {
            services.AddOptions<SubmissionRateLimitSettings>()
                .BindConfiguration(SubmissionRateLimitSettings.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<AuthRateLimitSettings>()
                .BindConfiguration(AuthRateLimitSettings.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Partition per user: each submission occupies a sandbox container, and users can share an IP
                options.AddPolicy(SubmissionsPolicy, httpContext =>
                {
                    var settings = httpContext.RequestServices
                        .GetRequiredService<IOptions<SubmissionRateLimitSettings>>().Value;

                    return FixedWindow(
                        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                        settings.PermitLimit,
                        settings.WindowSeconds);
                });

                // Registration, per client IP (the real one when behind a trusted proxy, see ForwardedHeaders)
                options.AddPolicy(AuthPolicy, httpContext =>
                {
                    var settings = GetAuthSettings(httpContext);
                    return FixedWindow(ClientIp(httpContext), settings.PermitLimit, settings.WindowSeconds);
                });

                // Login, per client IP and normalized email: guessing one account's password is slowed down
                // without letting one noisy address lock everyone behind a shared IP out of their accounts
                options.AddPolicy(LoginPolicy, httpContext =>
                {
                    var settings = GetAuthSettings(httpContext);
                    var email = httpContext.Items[LoginRateLimitKeyMiddleware.EmailItemKey] as string ?? string.Empty;

                    return FixedWindow($"{ClientIp(httpContext)}|{email}", settings.PermitLimit, settings.WindowSeconds);
                });

                // Plus a looser per-IP ceiling on login across all emails, against password spraying.
                // The global limiter runs before endpoint policies; every other endpoint is unlimited here
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    if (PolicyName(httpContext) != LoginPolicy)
                    {
                        return RateLimitPartition.GetNoLimiter(string.Empty);
                    }

                    var settings = GetAuthSettings(httpContext);
                    return FixedWindow(ClientIp(httpContext), settings.EffectiveLoginPerIpPermitLimit, settings.WindowSeconds);
                });

                options.OnRejected = async (context, _) =>
                {
                    var httpContext = context.HttpContext;

                    var detail = PolicyName(httpContext) switch
                    {
                        LoginPolicy => "Too many login attempts. Please wait a moment and try again.",
                        AuthPolicy => "Too many registration attempts. Please wait a moment and try again.",
                        SubmissionsPolicy => "Too many submissions. Please wait a moment before submitting again.",
                        _ => "Too many requests. Please wait a moment and try again."
                    };

                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        httpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

                    await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        ProblemDetails = new ProblemDetails
                        {
                            Type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
                            Title = "Too Many Requests",
                            Status = StatusCodes.Status429TooManyRequests,
                            Detail = detail,
                            Instance = httpContext.Request.Path
                        }
                    });
                };
            });

            return services;
        }
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0
        });

    private static AuthRateLimitSettings GetAuthSettings(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitSettings>>().Value;

    private static string ClientIp(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string? PolicyName(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
}
