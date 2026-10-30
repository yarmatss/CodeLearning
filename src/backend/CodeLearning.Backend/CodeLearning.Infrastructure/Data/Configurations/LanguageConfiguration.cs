using CodeLearning.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CodeLearning.Infrastructure.Data.Configurations;

public class LanguageConfiguration : IEntityTypeConfiguration<Language>
{
    private const string RunTestsCommand = "/bin/bash /app/run_tests.sh";

    private sealed record Runtime(string DockerImage, string FileExtension, string ExecutableCommand, string? CompileCommand);

    private sealed record Limits(int TimeoutSeconds, int MemoryLimitMb, decimal CpuLimit);

    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.HasIndex(l => new { l.Name, l.Version })
            .IsUnique();

        builder.HasData(
            CreateLanguage("11111111-1111-1111-1111-111111111111", "Python", "3.12",
                new Runtime("codelearning/python:3.12-slim", ".py", "python3", CompileCommand: null),
                new Limits(TimeoutSeconds: 5, MemoryLimitMb: 256, CpuLimit: 0.5m)),
            CreateLanguage("22222222-2222-2222-2222-222222222222", "JavaScript", "22",
                new Runtime("codelearning/node:22-slim", ".js", "node", CompileCommand: null),
                new Limits(TimeoutSeconds: 5, MemoryLimitMb: 256, CpuLimit: 0.5m)),
            // "csharp" is a special marker handled in the generated shell script
            CreateLanguage("33333333-3333-3333-3333-333333333333", "C#", "14",
                new Runtime("codelearning/dotnet:10.0", ".cs", "dotnet run --no-build", CompileCommand: "csharp"),
                new Limits(TimeoutSeconds: 10, MemoryLimitMb: 512, CpuLimit: 1.0m)),
            // javac file name must match the capitalized Solution class
            CreateLanguage("44444444-4444-4444-4444-444444444444", "Java", "21",
                new Runtime("codelearning/java:21-jdk", ".java", "java Solution", CompileCommand: "javac Solution.java"),
                new Limits(TimeoutSeconds: 10, MemoryLimitMb: 512, CpuLimit: 1.0m)));
    }

    private static Language CreateLanguage(string id, string name, string version, Runtime runtime, Limits limits) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        Version = version,
        DockerImage = runtime.DockerImage,
        FileExtension = runtime.FileExtension,
        RunCommand = RunTestsCommand,
        ExecutableCommand = runtime.ExecutableCommand,
        CompileCommand = runtime.CompileCommand,
        TimeoutSeconds = limits.TimeoutSeconds,
        MemoryLimitMB = limits.MemoryLimitMb,
        CpuLimit = limits.CpuLimit,
        IsEnabled = true,
        CreatedAt = SeedConstants.SeedDate,
        UpdatedAt = SeedConstants.SeedDate
    };
}
