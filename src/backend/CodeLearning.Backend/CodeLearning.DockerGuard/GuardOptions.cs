namespace CodeLearning.DockerGuard;

public class GuardOptions
{
    public const string SectionName = "DockerGuard";

    public const string SandboxLabel = "codelearning.sandbox";

    public string DockerSocket { get; set; } = "/var/run/docker.sock";

    // Executor images the Runner may pull and start; anything else is refused
    public string[] AllowedImages { get; set; } = [];

    // Extra OCI runtimes (e.g. "runsc" for gVisor); the daemon's default runtime is always allowed
    public string[] AllowedRuntimes { get; set; } = [];

    // The only directory the Runner may upload the workspace to
    public string WorkspacePath { get; set; } = "/app";

    // Ceilings for a sandbox's resources, above the largest per-language limit the Runner requests
    public long MaxMemoryBytes { get; set; } = 1024L * 1024 * 1024;

    public long MaxNanoCpus { get; set; } = 2_000_000_000;

    public long MaxPidsLimit { get; set; } = 256;

    public long MaxTmpfsSizeMb { get; set; } = 64;

    // Upper bound for every ulimit value (e.g. fsize, nofile)
    public long MaxUlimitValue { get; set; } = 64L * 1024 * 1024;
}
