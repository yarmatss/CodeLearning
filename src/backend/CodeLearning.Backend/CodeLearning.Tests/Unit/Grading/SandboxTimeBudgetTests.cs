using CodeLearning.Core.Entities;
using CodeLearning.Core.Grading;
using FluentAssertions;

namespace CodeLearning.Tests.Unit.Grading;

public class SandboxTimeBudgetTests
{
    private static Language Lang(string name, int timeoutSeconds, string? compileCommand) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Version = "1",
        DockerImage = "img",
        FileExtension = ".x",
        RunCommand = "run",
        ExecutableCommand = "run",
        CompileCommand = compileCommand,
        TimeoutSeconds = timeoutSeconds,
        MemoryLimitMB = 256
    };

    public static TheoryData<string, int, string?, int> Languages => new()
    {
        { "Python", 5, null, 54 },
        { "JavaScript", 5, null, 54 },
        { "Java", 10, "javac Solution.java", 24 },
        { "C#", 10, "dotnet build", 18 }
    };

    [Theory]
    [MemberData(nameof(Languages))]
    public void MaxTestCount_MatchesWhatTheRunnerCanActuallyRun(string name, int timeout, string? compile, int expectedMax)
    {
        var language = Lang(name, timeout, compile);
        const int cap = SandboxTimeBudget.DefaultMaxContainerRuntimeSeconds;

        var max = SandboxTimeBudget.MaxTestCount(language, cap);

        max.Should().Be(expectedMax);

        // Every accepted test gets its full time limit from the Runner's budget...
        SandboxTimeBudget.For(language, max, cap).TestsMs.Should().Be(max * timeout * 1000);
        // ...and one more test would be cut short, which is why the API rejects it
        SandboxTimeBudget.For(language, max + 1, cap).TestsMs.Should().BeLessThan((max + 1) * timeout * 1000);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void For_NeverExceedsTheContainerCap(string name, int timeout, string? compile, int _)
    {
        var language = Lang(name, timeout, compile);

        foreach (var tests in new[] { 1, 10, 50, 500 })
        {
            SandboxTimeBudget.For(language, tests, 300).ContainerSeconds.Should().BeLessThanOrEqualTo(300);
        }
    }
}
