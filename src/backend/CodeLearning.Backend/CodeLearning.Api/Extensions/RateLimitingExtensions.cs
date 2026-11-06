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

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.PermitLimit,
                            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                            QueueLimit = 0
                        });
                });

                // Partition per client IP: these requests are anonymous, and this slows down password
                // guessing and credential stuffing across many accounts
                options.AddPolicy(AuthPolicy, httpContext =>
                {
                    var settings = httpContext.RequestServices
                        .GetRequiredService<IOptions<AuthRateLimitSettings>>().Value;

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.PermitLimit,
                            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                            QueueLimit = 0
                        });
                });

                options.OnRejected = async (context, _) =>
                {
                    var httpContext = context.HttpContext;

                    var policy = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                    var detail = policy == AuthPolicy
                        ? "Too many attempts. Please wait a moment and try again."
                        : "Too many submissions. Please wait a moment before submitting again.";

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
}
