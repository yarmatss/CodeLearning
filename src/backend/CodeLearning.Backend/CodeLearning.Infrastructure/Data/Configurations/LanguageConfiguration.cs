using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    private const string RunTestsCommand = "/bin/bash /app/run_tests.sh";

    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.HasIndex(l => new { l.Name, l.Version })
            .IsUnique();

        builder.HasData(
            CreateLanguage("11111111-1111-1111-1111-111111111111", "Python", "3.12",
                "codelearning/python:3.12-slim", ".py", "python3", compileCommand: null,
                timeoutSeconds: 5, memoryLimitMb: 256, cpuLimit: 0.5m),
            CreateLanguage("22222222-2222-2222-2222-222222222222", "JavaScript", "22",
                "codelearning/node:22-slim", ".js", "node", compileCommand: null,
                timeoutSeconds: 5, memoryLimitMb: 256, cpuLimit: 0.5m),
            // "csharp" is a special marker handled in the generated shell script
            CreateLanguage("33333333-3333-3333-3333-333333333333", "C#", "14",
                "codelearning/dotnet:10.0", ".cs", "dotnet run --no-build", compileCommand: "csharp",
                timeoutSeconds: 10, memoryLimitMb: 512, cpuLimit: 1.0m),
            // javac file name must match the capitalized Solution class
            CreateLanguage("44444444-4444-4444-4444-444444444444", "Java", "21",
                "codelearning/java:21-jdk", ".java", "java Solution", compileCommand: "javac Solution.java",
                timeoutSeconds: 10, memoryLimitMb: 512, cpuLimit: 1.0m));
    }

    private static Language CreateLanguage(
        string id,
        string name,
        string version,
        string dockerImage,
        string fileExtension,
        string executableCommand,
        string? compileCommand,
        int timeoutSeconds,
        int memoryLimitMb,
        decimal cpuLimit) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Version = version,
        DockerImage = dockerImage,
        FileExtension = fileExtension,
        RunCommand = RunTestsCommand,
        ExecutableCommand = executableCommand,
        CompileCommand = compileCommand,
        TimeoutSeconds = timeoutSeconds,
        MemoryLimitMB = memoryLimitMb,
        CpuLimit = cpuLimit,
        IsEnabled = true,
        CreatedAt = SeedConstants.SeedDate,
        UpdatedAt = SeedConstants.SeedDate
    };
}
