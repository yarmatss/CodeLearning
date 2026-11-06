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

    public static RouteDecision Evaluate(string method, string path, IQueryCollection query, GuardOptions options)
    {
        // Encoded or relative segments could be read differently by the daemon than by this check
        if (path.Contains('%') || path.Contains("..") || path.Contains("//") || path.Contains('\\'))
        {
            return RouteDecision.Deny("Path is not allowed");
        }

        path = VersionPrefix().Replace(path, string.Empty);

        if (path is "/_ping" && method is "GET" or "HEAD")
        {
            return new RouteDecision(RouteKind.Forward);
        }

        if (path is "/version" && method is "GET")
        {
            return new RouteDecision(RouteKind.Forward);
        }

        if (method is "GET" && ImageInspect().IsMatch(path))
        {
            return new RouteDecision(RouteKind.Forward);
        }

        if (path is "/images/create" && method is "POST")
        {
            return IsAllowedPull(query, options)
                ? new RouteDecision(RouteKind.Forward)
                : RouteDecision.Deny("Image is not allowed");
        }

        if (path is "/containers/create" && method is "POST")
        {
            return new RouteDecision(RouteKind.CreateContainer);
        }

        var match = ContainerRoute().Match(path);
        if (!match.Success || match.Groups["id"].Value is "create" or "json" or "prune")
        {
            return RouteDecision.Deny("Endpoint is not allowed");
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
            : RouteDecision.Deny("Endpoint is not allowed");
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
