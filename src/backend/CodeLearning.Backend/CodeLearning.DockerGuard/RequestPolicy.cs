using Microsoft.Extensions.Primitives;
using System.Text.RegularExpressions;

namespace CodeLearning.DockerGuard;

public enum RouteKind
{
    Deny,
    Forward,
    CreateContainer,
    SandboxContainer
}

public sealed record RouteDecision(RouteKind Kind, string? ContainerId = null, string? Reason = null)
{
    public static RouteDecision Deny(string reason) => new(RouteKind.Deny, Reason: reason);
}

// Allowlist of the Docker API calls DockerRunner makes; every other endpoint is refused
public static partial class RequestPolicy
{
    [GeneratedRegex(@"^/v\d+\.\d+(?=/)")]
    private static partial Regex VersionPrefix();

    [GeneratedRegex(@"^/images/[^/]+(/[^/]+)*/json$")]
    private static partial Regex ImageInspect();

    [GeneratedRegex(@"^/containers/(?<id>[A-Za-z0-9][A-Za-z0-9_.-]*)(?<action>/start|/wait|/kill|/logs|/archive)?$")]
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
            ?? EvaluateContainerRoute(method, path, query, options);
    }

    // Routes that don't target an existing container; null when the path is none of them
    private static RouteDecision? EvaluateDaemonRoute(string method, string path, IQueryCollection query, GuardOptions options) =>
        (path, method) switch
        {
            ("/_ping", "GET" or "HEAD") => ForwardDecision,
            ("/version", "GET") => ForwardDecision,
            ("/images/create", "POST") => IsAllowedPull(query, options)
                ? ForwardDecision
                : RouteDecision.Deny("Image is not allowed"),
            ("/containers/create", "POST") => new RouteDecision(RouteKind.CreateContainer),
            (_, "GET") when ImageInspect().IsMatch(path) => ForwardDecision,
            _ => null
        };

    private static RouteDecision EvaluateContainerRoute(string method, string path, IQueryCollection query, GuardOptions options)
    {
        var match = ContainerRoute().Match(path);
        if (!match.Success || match.Groups["id"].Value is "create" or "json" or "prune")
        {
            return NotAllowed;
        }

        var id = match.Groups["id"].Value;
        var allowed = (match.Groups["action"].Value, method) switch
        {
            ("/start" or "/wait" or "/kill", "POST") => true,
            ("/logs", "GET") => !IsTrue(query["follow"]),
            ("/archive", "PUT") => query["path"].ToString() == options.WorkspacePath,
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

    private static bool IsTrue(StringValues value) =>
        value.ToString() is "1" or "true" or "True";
}
