using System.Globalization;
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
        "Image", "Cmd", "WorkingDir", "User", "Labels", "HostConfig",
        // The Runner streams the job archive on stdin
        "AttachStdin", "OpenStdin", "StdinOnce"
    };

    private static readonly HashSet<string> HostConfigFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "NetworkMode", "Memory", "MemorySwap", "NanoCpus", "PidsLimit", "Tmpfs",
        "CapDrop", "CapAdd", "SecurityOpt", "Ulimits", "AutoRemove", "Runtime", "ReadonlyRootfs"
    };

    // What a uid 0 sandbox may add back after dropping ALL: switching to the student user (SETUID, SETGID) and
    // signalling its processes (KILL). None of them reaches past the container's own namespaces
    private static readonly HashSet<string> RootCapabilities = new(StringComparer.Ordinal)
    {
        "SETUID", "SETGID", "KILL"
    };

    // Mount options a tmpfs may carry: a size cap is required, the rest only restrict
    private static readonly HashSet<string> AllowedTmpfsOptions = new(StringComparer.Ordinal)
    {
        "size", "mode", "uid", "gid", "noexec", "nosuid", "nodev", "rw"
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
            if (CheckStructure(document.RootElement) is { } structureError)
            {
                return ValidationResult.Fail(structureError);
            }

            request = document.RootElement.Deserialize<CreateContainerRequest>(ReadOptions);
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

    // Shape checks on the raw JSON, before it is bound to the model and its unchecked fields are lost
    private static string? CheckStructure(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "Body must be a JSON object";
        }

        if (FindDuplicate(root) is { } duplicate)
        {
            return $"Duplicate field '{duplicate}'";
        }

        if (FindUnexpected(root, TopLevelFields) is { } unexpected)
        {
            return $"Field '{unexpected}' is not allowed";
        }

        if (FindProperty(root, "HostConfig") is { ValueKind: JsonValueKind.Object } hostConfig
            && FindUnexpected(hostConfig, HostConfigFields) is { } unexpectedHost)
        {
            return $"HostConfig field '{unexpectedHost}' is not allowed";
        }

        return null;
    }

    private static string? CheckRules(CreateContainerRequest request, GuardOptions options)
    {
        if (CheckContainer(request, options) is { } error)
        {
            return error;
        }

        return request.HostConfig is { } host
            ? CheckUser(request.User, host, options) ?? CheckIsolation(host, options) ?? CheckLimits(host, options)
            : "HostConfig is required";
    }

    private static string? CheckContainer(CreateContainerRequest request, GuardOptions options)
    {
        if (request.Image is null || !options.AllowedImages.Contains(request.Image, StringComparer.Ordinal))
        {
            return "Image is not allowed";
        }

        if (request.Labels is null
            || !request.Labels.TryGetValue(GuardOptions.SandboxLabel, out var sandbox) || sandbox != "true")
        {
            return $"Label '{GuardOptions.SandboxLabel}=true' is required";
        }

        return null;
    }

    // What separates the container from the host: network, capabilities, security options, runtime
    private static string? CheckIsolation(CreateHostConfig host, GuardOptions options)
    {
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

        if (host.Runtime is not null && !options.AllowedRuntimes.Contains(host.Runtime, StringComparer.Ordinal))
        {
            return "Runtime is not allowed";
        }

        if (host.ReadonlyRootfs != true)
        {
            return "ReadonlyRootfs is required";
        }

        return null;
    }

    // Exactly the configured sandbox user with no capabilities at all, or uid 0 with every capability dropped
    // except the ones needed to drop to that user. Uids are compared as numbers ("00" is root), and names are
    // refused because the image's /etc/passwd decides what they mean
    private static string? CheckUser(string? user, CreateHostConfig host, GuardOptions options)
    {
        if (!TryParseUser(user, out var uid, out var gid))
        {
            return "User must be a numeric uid:gid";
        }

        var capAdd = (host.CapAdd ?? []).Select(NormalizeCapability).ToList();

        if (uid == options.SandboxUid)
        {
            if (gid != options.SandboxGid)
            {
                return $"User must be {options.SandboxUid}:{options.SandboxGid}";
            }

            return capAdd.Count == 0 ? null : "CapAdd is not allowed for the sandbox user";
        }

        if (uid == 0 && gid is null or 0)
        {
            if (host.CapDrop is null || !host.CapDrop.Contains("ALL", StringComparer.OrdinalIgnoreCase))
            {
                return "Root requires CapDrop ALL";
            }

            return capAdd.All(RootCapabilities.Contains)
                ? null
                : "Root may only add SETUID, SETGID and KILL";
        }

        return $"User must be {options.SandboxUid}:{options.SandboxGid} or 0:0";
    }

    public static bool TryParseUser(string? user, out long uid, out long? gid)
    {
        uid = -1;
        gid = null;

        if (string.IsNullOrEmpty(user))
        {
            return false;
        }

        var parts = user.Split(':');
        if (parts.Length > 2 || !TryParseId(parts[0], out uid))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!TryParseId(parts[1], out var parsedGid))
            {
                return false;
            }

            gid = parsedGid;
        }

        return true;
    }

    // Digits only: no sign, whitespace or names; leading zeros are allowed and parsed away, as Docker does
    private static bool TryParseId(string value, out long id)
    {
        id = -1;
        return value.Length is > 0 and <= 10
               && value.All(char.IsAsciiDigit)
               && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id)
               && id <= uint.MaxValue;
    }

    private static string NormalizeCapability(string capability)
    {
        var upper = capability.Trim().ToUpperInvariant();
        return upper.StartsWith("CAP_", StringComparison.Ordinal) ? upper["CAP_".Length..] : upper;
    }

    // Resource limits, so one submission cannot exhaust the host
    private static string? CheckLimits(CreateHostConfig host, GuardOptions options)
    {
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

        if (host.Memory > options.MaxMemoryBytes
            || host.NanoCpus > options.MaxNanoCpus
            || host.PidsLimit > options.MaxPidsLimit)
        {
            return "Memory, NanoCpus or PidsLimit exceeds the allowed maximum";
        }

        if (host.Ulimits?.Any(u => u.Soft < 0 || u.Hard < 0 || u.Soft > options.MaxUlimitValue || u.Hard > options.MaxUlimitValue) == true)
        {
            return "Ulimits exceed the allowed maximum";
        }

        return CheckTmpfs(host.Tmpfs, options);
    }

    // Each tmpfs is RAM-backed, so it needs an explicit size within the cap
    private static string? CheckTmpfs(Dictionary<string, string>? tmpfs, GuardOptions options)
    {
        if (tmpfs is null || tmpfs.Count == 0)
        {
            return "Tmpfs is required for the writable paths of a read-only container";
        }

        foreach (var mountOptions in tmpfs.Values)
        {
            var parsed = mountOptions.Split(',', StringSplitOptions.TrimEntries);
            if (parsed.Any(o => !AllowedTmpfsOptions.Contains(o.Split('=', 2)[0])))
            {
                return "Tmpfs option is not allowed";
            }

            var size = parsed
                .FirstOrDefault(o => o.StartsWith("size=", StringComparison.Ordinal))?["size=".Length..];

            if (size is null || !TryParseSizeMb(size, out var sizeMb) || sizeMb > options.MaxTmpfsSizeMb)
            {
                return $"Tmpfs needs a size of at most {options.MaxTmpfsSizeMb}m";
            }
        }

        return null;
    }

    private static bool TryParseSizeMb(string size, out double sizeMb)
    {
        sizeMb = 0;
        if (size.Length == 0)
        {
            return false;
        }

        var (number, divisor) = char.ToLowerInvariant(size[^1]) switch
        {
            'k' => (size[..^1], 1024.0),
            'm' => (size[..^1], 1.0),
            'g' => (size[..^1], 1.0 / 1024),
            _ when char.IsDigit(size[^1]) => (size, 1024.0 * 1024),
            _ => (string.Empty, 0.0)
        };

        if (!long.TryParse(number, out var value) || value <= 0)
        {
            return false;
        }

        sizeMb = value / divisor;
        return true;
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

    private static JsonElement? FindProperty(JsonElement element, string name) =>
        element.EnumerateObject()
            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(p => (JsonElement?)p.Value)
            .FirstOrDefault();
}

public sealed class CreateContainerRequest
{
    public string? Image { get; set; }
    public List<string>? Cmd { get; set; }
    public string? WorkingDir { get; set; }
    public string? User { get; set; }
    public bool? AttachStdin { get; set; }
    public bool? OpenStdin { get; set; }
    public bool? StdinOnce { get; set; }
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
    public List<string>? CapAdd { get; set; }
    public bool? ReadonlyRootfs { get; set; }
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
