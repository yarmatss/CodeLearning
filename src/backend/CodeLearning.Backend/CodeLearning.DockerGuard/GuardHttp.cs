using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodeLearning.DockerGuard;

public static class GuardHttp
{
    // Keeps quotes in the reason readable instead of ' escapes
    private static readonly JsonSerializerOptions ErrorJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task DenyAsync(HttpContext context, string reason)
    {
        // Docker's error shape, so Docker.DotNet surfaces the reason in its DockerApiException
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { message = $"DockerGuard: {reason}" }, ErrorJsonOptions),
            context.RequestAborted);
    }

    // Returns null when the body is larger than maxBytes, without buffering more than that
    public static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }
}
