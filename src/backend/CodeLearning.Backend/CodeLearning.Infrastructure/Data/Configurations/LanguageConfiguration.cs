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
            new Language
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Python",
                Version = "3.12",
                DockerImage = "codelearning/python:3.12-slim",
                FileExtension = ".py",
                RunCommand = RunTestsCommand,
                ExecutableCommand = "python3",
                CompileCommand = null,
                TimeoutSeconds = 5,
                MemoryLimitMB = 256,
                CpuLimit = 0.5m,
                IsEnabled = true,
                CreatedAt = SeedConstants.SeedDate,
                UpdatedAt = SeedConstants.SeedDate
            },
            new Language
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "JavaScript",
                Version = "22",
                DockerImage = "codelearning/node:22-slim",
                FileExtension = ".js",
                RunCommand = RunTestsCommand,
                ExecutableCommand = "node",
                CompileCommand = null,
                TimeoutSeconds = 5,
                MemoryLimitMB = 256,
                CpuLimit = 0.5m,
                IsEnabled = true,
                CreatedAt = SeedConstants.SeedDate,
                UpdatedAt = SeedConstants.SeedDate
            },
            new Language
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "C#",
                Version = "14",
                DockerImage = "codelearning/dotnet:10.0",
                FileExtension = ".cs",
                RunCommand = RunTestsCommand,
                ExecutableCommand = "dotnet run --no-build",
                CompileCommand = "csharp",  // Special marker - handled in shell script
                TimeoutSeconds = 10,
                MemoryLimitMB = 512,
                CpuLimit = 1.0m,
                IsEnabled = true,
                CreatedAt = SeedConstants.SeedDate,
                UpdatedAt = SeedConstants.SeedDate
            },
            new Language
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Java",
                Version = "21",
                DockerImage = "codelearning/java:21-jdk",
                FileExtension = ".java",
                RunCommand = RunTestsCommand,
                ExecutableCommand = "java Solution",
                CompileCommand = "javac Solution.java",  // Must match capitalized filename
                TimeoutSeconds = 10,
                MemoryLimitMB = 512,
                CpuLimit = 1.0m,
                IsEnabled = true,
                CreatedAt = SeedConstants.SeedDate,
                UpdatedAt = SeedConstants.SeedDate
            });
    }
}
