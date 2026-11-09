using CodeLearning.DockerGuard;
using Microsoft.Extensions.Options;
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
            await GuardHttp.DenyAsync(context, decision.Reason!);
            return;

        case RouteKind.SandboxContainer or RouteKind.AttachSandboxContainer:
            if (!await docker.IsSandboxContainerAsync(decision.ContainerId!, context.RequestAborted))
            {
                app.Logger.LogWarning("Refused {Method} {Path}: not a sandbox container", request.Method, request.Path);
                await GuardHttp.DenyAsync(context, "Only sandbox containers can be managed");
                return;
            }

            if (decision.Kind == RouteKind.AttachSandboxContainer)
            {
                await AttachProxy.ProxyAsync(context, options.DockerSocket, app.Logger);
                return;
            }
            break;

        case RouteKind.ListSandboxContainers:
            // The caller's filters are replaced, so the listing never shows other containers
            request.QueryString = RequestPolicy.SandboxListQuery(request.Query);
            break;

        case RouteKind.CreateContainer:
            var body = await GuardHttp.ReadBodyAsync(request, MaxCreateBodyBytes, context.RequestAborted);
            if (body is null)
            {
                await GuardHttp.DenyAsync(context, "Body is too large");
                return;
            }

            var result = ContainerCreateValidator.Validate(body, options);
            if (!result.IsValid)
            {
                app.Logger.LogWarning("Refused container create: {Reason}", result.Error);
                await GuardHttp.DenyAsync(context, result.Error!);
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
