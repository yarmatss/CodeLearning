using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeLearning.DockerGuard;

public enum RouteKind
{
    Deny,
    Forward,
    CreateContainer,
    SandboxContainer,
    // A sandbox container route that is tunnelled by AttachProxy instead of forwarded by YARP
    AttachSandboxContainer,
    // GET /containers/json, forwarded with the sandbox label filter forced (see SandboxListQuery)
    ListSandboxContainers
}

public sealed record RouteDecision(RouteKind Kind, string? ContainerId = null, string? Reason = null)
{
    public static RouteDecision Deny(string reason) => new(RouteKind.Deny, Reason: reason);
}

// Allowlist of the Docker API calls DockerRunner and SandboxContainerCleanupService make; every other endpoint is refused
public static partial class RequestPolicy
{
    [GeneratedRegex(@"^/v\d+\.\d+(?=/)")]
    private static partial Regex VersionPrefix();

    [GeneratedRegex(@"^/images/[^/]+(/[^/]+)*/json$")]
    private static partial Regex ImageInspect();

    [GeneratedRegex(@"^/containers/(?<id>[A-Za-z0-9][A-Za-z0-9_.-]*)(?<action>/start|/wait|/kill|/logs|/attach|/json)?$")]
    private static partial Regex ContainerRoute();

    private static readonly RouteDecision ForwardDecision = new(RouteKind.Forward);
    private static readonly RouteDecision NotAllowed = RouteDecision.Deny("Endpoint is not allowed");

    public static RouteDecision Evaluate(string method, string path, IQueryCollection query, GuardOptions options)
    {
        // Encoded or relative segments could be read differently by the daemon than by this check
        if (path.Contains('%') || path.Contains("..") || path.Contains("//") || path.Contains('\\'))
        {
            return RouteDecision.Deny("Path is not allowed");
        }

        path = VersionPrefix().Replace(path, string.Empty);

        return EvaluateDaemonRoute(method, path, query, options)
            ?? EvaluateContainerRoute(method, path, query);
    }

    // The query forwarded for a container listing: only sandbox containers, whatever filters the caller sent
    public static QueryString SandboxListQuery(IQueryCollection query)
    {
        var filters = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, bool>>
        {
            ["label"] = new() { [$"{GuardOptions.SandboxLabel}=true"] = true }
        });

        var builder = new QueryBuilder { { "filters", filters } };
        if (IsTrue(query["all"]))
        {
            builder.Add("all", "1");
        }

        return builder.ToQueryString();
    }

    // Routes that don't target an existing container; null when the path is none of them
    private static RouteDecision? EvaluateDaemonRoute(string method, string path, IQueryCollection query, GuardOptions options) =>
        (path, method) switch
        {
            // Also the compose healthcheck of the guard
            ("/_ping", "GET" or "HEAD") => ForwardDecision,
            ("/version", "GET") => ForwardDecision,
            ("/images/create", "POST") => IsAllowedPull(query, options)
                ? ForwardDecision
                : RouteDecision.Deny("Image is not allowed"),
            ("/containers/create", "POST") => new RouteDecision(RouteKind.CreateContainer),
            ("/containers/json", "GET") => new RouteDecision(RouteKind.ListSandboxContainers),
            (_, "GET") when ImageInspect().IsMatch(path) => ForwardDecision,
            _ => null
        };

    private static RouteDecision EvaluateContainerRoute(string method, string path, IQueryCollection query)
    {
        var match = ContainerRoute().Match(path);
        if (!match.Success || match.Groups["id"].Value is "create" or "json" or "prune")
        {
            return NotAllowed;
        }

        var id = match.Groups["id"].Value;

        // The job archive is streamed to stdin; the container's output is read from the logs
        if (match.Groups["action"].Value == "/attach")
        {
            return method == "POST" && IsTrue(query["stdin"])
                ? new RouteDecision(RouteKind.AttachSandboxContainer, id)
                : NotAllowed;
        }

        var allowed = (match.Groups["action"].Value, method) switch
        {
            ("/start" or "/wait" or "/kill", "POST") => true,
            // Inspect: Docker.DotNet reads the container's TTY setting before demultiplexing its logs
            ("/json", "GET") => true,
            // A followed log stream would hold the connection for as long as the container lives
            ("/logs", "GET") => !IsTrue(query["follow"]),
            ("", "DELETE") => true,
            _ => false
        };

        return allowed
            ? new RouteDecision(RouteKind.SandboxContainer, id)
            : NotAllowed;
    }

    private static bool IsAllowedPull(IQueryCollection query, GuardOptions options)
    {
        if (query.ContainsKey("fromSrc") || query.ContainsKey("repo"))
        {
            return false;
        }

        var image = query["fromImage"].ToString();
        var tag = query["tag"].ToString();
        if (!string.IsNullOrEmpty(tag))
        {
            image = $"{image}:{tag}";
        }

        return options.AllowedImages.Contains(image, StringComparer.Ordinal);
    }

    // Docker reads any value other than "", "0" or "false" as true (it also accepts "no" and "none" as false;
    // reading those as true here only makes the guard stricter). Repeated parameters join into "a,b", which is true
    public static bool IsTrue(StringValues value)
    {
        var text = value.ToString().Trim();
        return text.Length > 0
               && text != "0"
               && !text.Equals("false", StringComparison.OrdinalIgnoreCase);
    }
}
