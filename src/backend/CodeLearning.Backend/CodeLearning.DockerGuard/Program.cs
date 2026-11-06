using CodeLearning.DockerGuard;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GuardOptions>().BindConfiguration(GuardOptions.SectionName);
builder.Services.AddHttpForwarder();
builder.Services.AddSingleton<DockerSocketClient>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<GuardOptions>>().Value;
var docker = app.Services.GetRequiredService<DockerSocketClient>();
var forwarder = app.Services.GetRequiredService<IHttpForwarder>();

// Waiting for a container lasts as long as the submission runs, well past YARP's default activity timeout
var requestConfig = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(15) };
const int MaxCreateBodyBytes = 1024 * 1024;

app.Run(async context =>
{
    var request = context.Request;
    var decision = RequestPolicy.Evaluate(request.Method, request.Path.Value ?? "/", request.Query, options);

    switch (decision.Kind)
    {
        case RouteKind.Deny:
            app.Logger.LogWarning("Refused {Method} {Path}: {Reason}", request.Method, request.Path, decision.Reason);
            await DenyAsync(context, decision.Reason!);
            return;

        case RouteKind.SandboxContainer:
            if (!await docker.IsSandboxContainerAsync(decision.ContainerId!, context.RequestAborted))
            {
                app.Logger.LogWarning("Refused {Method} {Path}: not a sandbox container", request.Method, request.Path);
                await DenyAsync(context, "Only sandbox containers can be managed");
                return;
            }
            break;

        case RouteKind.CreateContainer:
            var body = await ReadBodyAsync(request, MaxCreateBodyBytes, context.RequestAborted);
            if (body is null)
            {
                await DenyAsync(context, "Body is too large");
                return;
            }

            var result = ContainerCreateValidator.Validate(body, options);
            if (!result.IsValid)
            {
                app.Logger.LogWarning("Refused container create: {Reason}", result.Error);
                await DenyAsync(context, result.Error!);
                return;
            }

            // Forward the rebuilt body, never the original one
            request.Body = new MemoryStream(result.Body!);
            request.ContentLength = result.Body!.Length;
            request.Headers.Remove("Transfer-Encoding");
            request.ContentType = "application/json";
            break;
    }

    var error = await forwarder.SendAsync(context, DockerSocketClient.BaseAddress, docker.Invoker, requestConfig, HttpTransformer.Default);
    if (error != ForwarderError.None)
    {
        app.Logger.LogError(context.GetForwarderErrorFeature()?.Exception,
            "Forwarding {Method} {Path} failed: {Error}", request.Method, request.Path, error);
    }
});

app.Run();

static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
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

        buffer.Write(chunk, 0, read);
    }

    return buffer.ToArray();
}

static async Task DenyAsync(HttpContext context, string reason)
{
    // Docker's error shape, so Docker.DotNet surfaces the reason in its DockerApiException
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = $"DockerGuard: {reason}" }, ErrorJson.Options));
}

static class ErrorJson
{
    // Keeps quotes in the reason readable instead of ' escapes
    public static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

public partial class Program;
