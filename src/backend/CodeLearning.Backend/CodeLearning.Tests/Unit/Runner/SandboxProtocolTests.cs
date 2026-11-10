extern alias Runner;

using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Grading;
using FluentAssertions;
using Runner::CodeLearning.Runner.Services;

namespace CodeLearning.Tests.Unit.Runner;

// The result channel and the generated script, without Docker (the Docker-gated checks are in Integration/Sandbox)
public class SandboxProtocolTests
{
    private const string Nonce = "0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public void Parse_ResultBetweenDelimiters_ShouldBeRead()
    {
        var stdout = $"noise\n<<<CL-RESULT-{Nonce}>>>\n{{\"Status\":2,\"TestResults\":[{{\"TestIndex\":0,\"Status\":2,\"ActualOutput\":\"3\"}}]}}\n<<<CL-END-{Nonce}>>>\n";

        var result = DockerRunner.ParseExecutionOutput(stdout, Nonce, totalTests: 1);

        result.Should().NotBeNull();
        result!.Status.Should().Be(SubmissionStatus.Completed);
        result.TotalTests.Should().Be(1);
        result.TestResults.Single().ActualOutput.Should().Be("3");
    }

    [Theory]
    [InlineData("{\"Status\":2}")]
    [InlineData("<<<CL-RESULT-00000000000000000000000000000000>>>{\"Status\":2}<<<CL-END-00000000000000000000000000000000>>>")]
    [InlineData("<<<CL-RESULT-0123456789ABCDEF0123456789ABCDEF>>>{\"Status\":2}")]
    [InlineData("<<<CL-RESULT-0123456789ABCDEF0123456789ABCDEF>>>not json<<<CL-END-0123456789ABCDEF0123456789ABCDEF>>>")]
    [InlineData("<<<CL-RESULT-0123456789ABCDEF0123456789ABCDEF>>>{\"Status\":99}<<<CL-END-0123456789ABCDEF0123456789ABCDEF>>>")]
    [InlineData("")]
    public void Parse_WithoutAValidResultForThisNonce_ShouldReturnNull(string stdout)
    {
        DockerRunner.ParseExecutionOutput(stdout, Nonce, totalTests: 1).Should().BeNull();
    }

    [Fact]
    public void Parse_PartialResultStillRunning_ShouldBeTimeLimitExceededKeepingTheTests()
    {
        var stdout = $"<<<CL-RESULT-{Nonce}>>>{{\"Status\":1,\"TestResults\":[{{\"TestIndex\":0,\"Status\":2}}]}}<<<CL-END-{Nonce}>>>";

        var result = DockerRunner.ParseExecutionOutput(stdout, Nonce, totalTests: 3);

        result!.Status.Should().Be(SubmissionStatus.TimeLimitExceeded);
        result.TestResults.Should().HaveCount(1);
    }

    [Fact]
    public void Script_ShouldRunSubmittedCodeOnlyThroughSetprivAndUseLf()
    {
        var language = Python();
        var budget = SandboxTimeBudget.For(language, 3, 300);

        var script = UniversalExecutor.GenerateShellScript(SandboxLanguage.For(language), language, budget);

        script.Should().NotContain("\r");
        script.Should().Contain("--run 'setpriv --reuid=65534 --regid=65534 --clear-groups --inh-caps=-all -- env -C /sandbox HOME=/tmp TMPDIR=/tmp python3 solution.py'");
        script.Should().Contain("--input \"$JOB/tests\"").And.Contain("--out \"$OUT/result.json\"").And.Contain("--workdir /sandbox");
        script.Should().Contain($"--totaltimelimit {budget.TestsMs}");
        script.Should().Contain("<<<CL-RESULT-%s>>>").And.Contain("NONCE=$(cat \"$JOB/nonce\")");
    }

    [Fact]
    public void Script_ForJava_ShouldCompileAsTheStudentWithItsOwnBudget()
    {
        var java = new Language
        {
            Name = "Java", Version = "21", DockerImage = "codelearning/java:21-jdk", FileExtension = ".java",
            RunCommand = "", ExecutableCommand = "java Solution", CompileCommand = "javac Solution.java", TimeoutSeconds = 10
        };
        var budget = SandboxTimeBudget.For(java, 2, 300);

        var script = UniversalExecutor.GenerateShellScript(SandboxLanguage.For(java), java, budget);

        budget.CompileSeconds.Should().Be(SandboxTimeBudget.DefaultCompileSeconds);
        script.Should().Contain($"timeout --kill-after=2 {budget.CompileSeconds} \"${{STUDENT[@]}}\" /bin/bash -c 'javac");
        script.Should().Contain("java -XX:MaxRAMPercentage=75");
    }

    [Theory]
    [InlineData(5, 2, 300, 10_000)]
    // 300 - 20 overhead - 6 runner slack
    [InlineData(5, 100, 300, 274_000)]
    public void Budget_ShouldKeepTheContainerWithinTheMaximumRuntime(int timeoutSeconds, int tests, int max, int expectedTestsMs)
    {
        var budget = SandboxTimeBudget.For(Python(timeoutSeconds), tests, max);

        budget.TestsMs.Should().Be(expectedTestsMs);
        budget.ContainerSeconds.Should().BeLessThanOrEqualTo(max);
        budget.RunnerSeconds.Should().BeGreaterThan(budget.TestsMs / 1000);
    }

    [Fact]
    public void BashQuote_ShouldEscapeSingleQuotes()
    {
        UniversalExecutor.BashQuote("it's").Should().Be("'it'\\''s'");
    }

    private static Language Python(int timeoutSeconds = 5) => new()
    {
        Name = "Python", Version = "3.12", DockerImage = "codelearning/python:3.12-slim", FileExtension = ".py",
        RunCommand = "", ExecutableCommand = "python3", TimeoutSeconds = timeoutSeconds
    };
}
