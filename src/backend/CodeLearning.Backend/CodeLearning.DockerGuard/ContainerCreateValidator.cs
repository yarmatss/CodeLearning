using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeLearning.DockerGuard;

public sealed record ValidationResult(bool IsValid, string? Error, byte[]? Body)
{
    public static ValidationResult Fail(string error) => new(false, error, null);
}

// Checks a POST /containers/create body and rebuilds it from the checked fields only.
// Docker's decoder matches field names case-insensitively and keeps the last duplicate, so the original
// body is never forwarded: only what this class validated can reach the daemon
public static class ContainerCreateValidator
{
    private static readonly HashSet<string> TopLevelFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Image", "Cmd", "WorkingDir", "User", "Labels", "HostConfig"
    };

    private static readonly HashSet<string> HostConfigFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "NetworkMode", "Memory", "MemorySwap", "NanoCpus", "PidsLimit", "Tmpfs",
        "CapDrop", "SecurityOpt", "Ulimits", "AutoRemove", "Runtime"
    };

    private static readonly HashSet<string> AllowedSecurityOptions = new(StringComparer.Ordinal)
    {
        "no-new-privileges", "no-new-privileges:true"
    };

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ValidationResult Validate(ReadOnlySpan<byte> json, GuardOptions options)
    {
        CreateContainerRequest? request;
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ValidationResult.Fail("Body must be a JSON object");
            }

            if (FindDuplicate(root) is { } duplicate)
            {
                return ValidationResult.Fail($"Duplicate field '{duplicate}'");
            }

            if (FindUnexpected(root, TopLevelFields) is { } unexpected)
            {
                return ValidationResult.Fail($"Field '{unexpected}' is not allowed");
            }

            if (TryGetProperty(root, "HostConfig", out var hostConfig) && hostConfig.ValueKind == JsonValueKind.Object
                && FindUnexpected(hostConfig, HostConfigFields) is { } unexpectedHost)
            {
                return ValidationResult.Fail($"HostConfig field '{unexpectedHost}' is not allowed");
            }

            request = root.Deserialize<CreateContainerRequest>(ReadOptions);
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail($"Invalid JSON: {ex.Message}");
        }

        if (request is null)
        {
            return ValidationResult.Fail("Body is empty");
        }

        if (CheckRules(request, options) is { } error)
        {
            return ValidationResult.Fail(error);
        }

        return new ValidationResult(true, null, JsonSerializer.SerializeToUtf8Bytes(request, WriteOptions));
    }

    private static string? CheckRules(CreateContainerRequest request, GuardOptions options)
    {
        if (request.Image is null || !options.AllowedImages.Contains(request.Image, StringComparer.Ordinal))
        {
            return "Image is not allowed";
        }

        if (!IsNonRootUser(request.User))
        {
            return "Container must run as a non-root user";
        }

        if (request.Labels is null
            || !request.Labels.TryGetValue(GuardOptions.SandboxLabel, out var sandbox) || sandbox != "true")
        {
            return $"Label '{GuardOptions.SandboxLabel}=true' is required";
        }

        var host = request.HostConfig;
        if (host is null)
        {
            return "HostConfig is required";
        }

        if (host.NetworkMode != "none")
        {
            return "NetworkMode must be 'none'";
        }

        if (host.CapDrop is null || !host.CapDrop.Contains("ALL", StringComparer.OrdinalIgnoreCase))
        {
            return "CapDrop must contain ALL";
        }

        if (host.SecurityOpt is null || host.SecurityOpt.Count == 0
            || !host.SecurityOpt.All(AllowedSecurityOptions.Contains))
        {
            return "SecurityOpt must be exactly no-new-privileges";
        }

        if (host.Memory is not > 0 || host.PidsLimit is not > 0)
        {
            return "Memory and PidsLimit limits are required";
        }

        if (host.MemorySwap is { } swap && swap != host.Memory)
        {
            return "MemorySwap must equal Memory";
        }

        if (host.NanoCpus is not > 0)
        {
            return "NanoCpus limit is required";
        }

        if (host.Runtime is not null && !options.AllowedRuntimes.Contains(host.Runtime, StringComparer.Ordinal))
        {
            return "Runtime is not allowed";
        }

        return null;
    }

    private static bool IsNonRootUser(string? user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return false;
        }

        var parts = user.Split(':');
        return parts.Length <= 2 && parts.All(p => p.Length > 0 && p is not "0" and not "root");
    }

    // Recursive, case-insensitive: "Privileged" and "privileged" are the same field to Docker
    private static string? FindDuplicate(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                    {
                        return property.Name;
                    }

                    if (FindDuplicate(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (FindDuplicate(item) is { } nested)
                    {
                        return nested;
                    }
                }
                return null;

            default:
                return null;
        }
    }

    // A field outside the allowlist is refused unless it only restates Docker's default (null, false, 0, empty)
    private static string? FindUnexpected(JsonElement element, HashSet<string> allowed) =>
        element.EnumerateObject()
            .Where(p => !allowed.Contains(p.Name) && !IsDefault(p.Value))
            .Select(p => p.Name)
            .FirstOrDefault();

    private static bool IsDefault(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => true,
        JsonValueKind.Number => value.GetDouble() == 0,
        JsonValueKind.String => value.GetString()!.Length == 0,
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => value.EnumerateObject().All(p => IsDefault(p.Value)),
        _ => false
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

public sealed class CreateContainerRequest
{
    public string? Image { get; set; }
    public List<string>? Cmd { get; set; }
    public string? WorkingDir { get; set; }
    public string? User { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public CreateHostConfig? HostConfig { get; set; }
}

public sealed class CreateHostConfig
{
    public string? NetworkMode { get; set; }
    public long? Memory { get; set; }
    public long? MemorySwap { get; set; }
    public long? NanoCpus { get; set; }
    public long? PidsLimit { get; set; }
    public Dictionary<string, string>? Tmpfs { get; set; }
    public List<string>? CapDrop { get; set; }
    public List<string>? SecurityOpt { get; set; }
    public List<CreateUlimit>? Ulimits { get; set; }
    public bool? AutoRemove { get; set; }
    public string? Runtime { get; set; }
}

public sealed class CreateUlimit
{
    public string? Name { get; set; }
    public long Soft { get; set; }
    public long Hard { get; set; }
}
