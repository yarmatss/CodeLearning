using Yarp.ReverseProxy.Forwarder;

namespace CodeLearning.DockerGuard;

// Applies RequestPolicy to each request, then either refuses it, handles it here (attach), or forwards it
// to the Docker socket. Create bodies are validated and rebuilt; list queries are rewritten to sandbox-only
public sealed class GuardRequestHandler(
    GuardOptions options,
    DockerSocketClient docker,
    IHttpForwarder forwarder,
    ILogger logger)
{
    private const int MaxCreateBodyBytes = 1024 * 1024;

    // Waiting for a container lasts as long as the submission runs, well past YARP's default activity timeout
    private static readonly ForwarderRequestConfig RequestConfig = new() { ActivityTimeout = TimeSpan.FromMinutes(15) };

    public async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var decision = RequestPolicy.Evaluate(request.Method, request.Path.Value ?? "/", request.Query, options);

        var forward = decision.Kind switch
        {
            RouteKind.Deny => await DenyAsync(context, decision.Reason!),
            RouteKind.SandboxContainer or RouteKind.AttachSandboxContainer => await PrepareSandboxContainerAsync(context, decision),
            RouteKind.ListSandboxContainers => PrepareSandboxList(request),
            RouteKind.CreateContainer => await PrepareCreateAsync(context),
            _ => true
        };

        if (forward)
        {
            await ForwardAsync(context);
        }
    }

    private async Task<bool> DenyAsync(HttpContext context, string reason)
    {
        logger.LogWarning("Refused {Method} {Path}: {Reason}", context.Request.Method, context.Request.Path, reason);
        await GuardHttp.DenyAsync(context, reason);
        return false;
    }

    private async Task<bool> PrepareSandboxContainerAsync(HttpContext context, RouteDecision decision)
    {
        if (!await docker.IsSandboxContainerAsync(decision.ContainerId!, context.RequestAborted))
        {
            return await DenyAsync(context, "Only sandbox containers can be managed");
        }

        if (decision.Kind == RouteKind.AttachSandboxContainer)
        {
            await AttachProxy.ProxyAsync(context, options.DockerSocket, logger);
            return false;
        }

        return true;
    }

    // The caller's filters are replaced, so the listing never shows other containers
    private static bool PrepareSandboxList(HttpRequest request)
    {
        request.QueryString = RequestPolicy.SandboxListQuery(request.Query);
        return true;
    }

    private async Task<bool> PrepareCreateAsync(HttpContext context)
    {
        var request = context.Request;
        var body = await GuardHttp.ReadBodyAsync(request, MaxCreateBodyBytes, context.RequestAborted);
        if (body is null)
        {
            await GuardHttp.DenyAsync(context, "Body is too large");
            return false;
        }

        var result = ContainerCreateValidator.Validate(body, options);
        if (!result.IsValid)
        {
            logger.LogWarning("Refused container create: {Reason}", result.Error);
            await GuardHttp.DenyAsync(context, result.Error!);
            return false;
        }

        // Forward the rebuilt body, never the original one
        request.Body = new MemoryStream(result.Body!);
        request.ContentLength = result.Body!.Length;
        request.Headers.Remove("Transfer-Encoding");
        request.ContentType = "application/json";
        return true;
    }

    private async Task ForwardAsync(HttpContext context)
    {
        var error = await forwarder.SendAsync(
            context, DockerSocketClient.BaseAddress, docker.Invoker, RequestConfig, HttpTransformer.Default,
            context.RequestAborted);

        if (error != ForwarderError.None)
        {
            logger.LogError(context.GetForwarderErrorFeature()?.Exception,
                "Forwarding {Method} {Path} failed: {Error}", context.Request.Method, context.Request.Path, error);
        }
    }
}
