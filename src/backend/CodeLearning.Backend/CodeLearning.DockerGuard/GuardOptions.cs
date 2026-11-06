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
}
