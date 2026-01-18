namespace CodeLearning.Core.Entities;

public class Language : BaseEntity
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string DockerImage { get; set; }
    public required string FileExtension { get; set; }
    public required string RunCommand { get; set; } = "/bin/bash /app/run_tests.sh";
    public required string ExecutableCommand { get; set; }
    public string? CompileCommand { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public int MemoryLimitMB { get; set; } = 256;
    public decimal CpuLimit { get; set; } = 0.5m;
    public bool IsEnabled { get; set; } = true;

    public ICollection<StarterCode> StarterCodes { get; init; } = [];
    public ICollection<Submission> Submissions { get; init; } = [];
}
