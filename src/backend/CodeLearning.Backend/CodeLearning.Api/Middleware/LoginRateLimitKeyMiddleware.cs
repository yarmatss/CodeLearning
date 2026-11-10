using CodeLearning.Api.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace CodeLearning.Api.Middleware;

// Rate limiter partitions are chosen synchronously before model binding, so they cannot read the
// request body. For the login endpoint this reads the email once, ahead of the limiter, and leaves it
// in HttpContext.Items; the body is rewound so model binding still sees it
internal sealed class LoginRateLimitKeyMiddleware(RequestDelegate next)
{
    public const string EmailItemKey = "CodeLearning.LoginRateLimit.Email";

    // Real login bodies are tiny; anything larger shares the per-IP "no email" bucket instead
    private const long MaxInspectedBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context, ILookupNormalizer normalizer)
    {
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        if (policy == RateLimitingExtensions.LoginPolicy)
        {
            context.Items[EmailItemKey] = await ReadNormalizedEmailAsync(context, normalizer) ?? string.Empty;
        }

        await next(context);
    }

    private static async Task<string?> ReadNormalizedEmailAsync(HttpContext context, ILookupNormalizer normalizer)
    {
        var request = context.Request;
        if (!request.HasJsonContentType() || request.ContentLength is not (> 0 and <= MaxInspectedBodyBytes))
        {
            return null;
        }

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Model binding matches property names case-insensitively, so this must too
            var email = document.RootElement.EnumerateObject()
                .Where(property => property.Name.Equals("email", StringComparison.OrdinalIgnoreCase))
                .Select(property => (JsonElement?)property.Value)
                .FirstOrDefault();

            return email is { ValueKind: JsonValueKind.String } value
                ? normalizer.NormalizeEmail(value.GetString()!.Trim())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
