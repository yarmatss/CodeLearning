using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Runner.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RunnerExecutionContext = CodeLearning.Runner.Models.ExecutionContext;

namespace CodeLearning.Runner.Tests.Integration.Services;

[Trait("Category", "Integration")]
[Trait("Category", "Docker")]
public class DockerRunnerSecurityTests : IDisposable
{
    private readonly IDockerRunner _dockerRunner;
    private readonly string _tempWorkspace;

    public DockerRunnerSecurityTests()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ExecutionSettings:DockerHost", "unix:///var/run/docker.sock" },
                { "ExecutionSettings:WorkspaceBasePath", Path.GetTempPath() }
            });

        var configuration = configBuilder.Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<DockerRunner>();

        _dockerRunner = new DockerRunner(configuration, logger);

        _tempWorkspace = Path.Combine(Path.GetTempPath(), "docker-security-tests");
        if (Directory.Exists(_tempWorkspace))
        {
            Directory.Delete(_tempWorkspace, true);
        }
        Directory.CreateDirectory(_tempWorkspace);
    }

    [Fact]
    public async Task SecurityTest_NetworkAttack_ShouldBeBlocked()
    {
        // Arrange
        var code = @"
def solution():
    import urllib.request
    try:
        urllib.request.urlopen('http://google.com')
        print('SECURITY_BREACH')
    except Exception as e:
        print('Network blocked')
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.True(
            result.Status == SubmissionStatus.RuntimeError || result.Status == SubmissionStatus.Completed,
            $"Expected RuntimeError or Completed but got {result.Status}");

        if (result.Status == SubmissionStatus.Completed)
        {
            var output = result.TestResults.FirstOrDefault()?.ActualOutput ?? "";
            Assert.DoesNotContain("SECURITY_BREACH", output);
            Assert.Contains("Network blocked", output);
        }
    }

    [Fact]
    public async Task SecurityTest_ForkBomb_ShouldBeLimited()
    {
        // Arrange
        var code = @"
def solution():
    import os
    try:
        for i in range(100):
            os.fork()
        print('SECURITY_BREACH')
    except Exception as e:
        print('Fork limited')
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.True(
            result.Status == SubmissionStatus.RuntimeError || result.Status == SubmissionStatus.Completed,
            $"Expected RuntimeError or Completed but got {result.Status}");

        if (result.Status == SubmissionStatus.Completed)
        {
            var output = result.TestResults.FirstOrDefault()?.ActualOutput ?? "";
            Assert.DoesNotContain("SECURITY_BREACH", output);
        }
    }

    [Fact]
    public async Task SecurityTest_InfiniteLoop_ShouldTimeout()
    {
        // Arrange
        var code = @"
def solution():
    while True:
        pass
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.TimeLimitExceeded, result.Status);
        Assert.True(result.TotalExecutionTimeMs >= 5000, $"Expected >= 5000ms but got {result.TotalExecutionTimeMs}ms");
    }

    [Fact]
    public async Task SecurityTest_FilesystemWrite_ShouldBeDenied()
    {
        // Arrange
        var code = @"
def solution():
    try:
        with open('/etc/hosts', 'a') as f:
            f.write('hacked')
        print('SECURITY_BREACH')
    except Exception as e:
        print('Write blocked')
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        var output = result.TestResults.FirstOrDefault()?.ActualOutput ?? "";
        Assert.DoesNotContain("SECURITY_BREACH", output);
    }

    [Fact]
    public async Task SecurityTest_PrivilegeEscalation_ShouldBeDenied()
    {
        // Arrange
        var code = @"
def solution():
    import os
    try:
        os.setuid(0)
        print('SECURITY_BREACH')
    except Exception as e:
        print('Privilege escalation blocked')
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        var output = result.TestResults.FirstOrDefault()?.ActualOutput ?? "";
        Assert.DoesNotContain("SECURITY_BREACH", output);
    }

    [Fact]
    public async Task SecurityTest_ValidCode_ShouldExecuteSuccessfully()
    {
        // Arrange
        var code = @"
def solution():
    a, b = map(int, input().split())
    print(a + b)
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Single(result.TestResults);
        Assert.Equal("3", result.TestResults[0].ActualOutput);
        Assert.Equal(TestResultStatus.Passed, result.TestResults[0].Status);
    }

    [Theory]
    [InlineData("import socket; socket.socket()", "Network operations")]
    [InlineData("import subprocess; subprocess.run(['ls'])", "Subprocess execution")]
    [InlineData("open('/proc/self/mem', 'r')", "Memory access")]
    public async Task SecurityTest_DangerousOperations_ShouldBeDenied(
        string dangerousCode,
        string description)
    {
        // Arrange
        var code = $@"
def solution():
    try:
        {dangerousCode}
        print('SECURITY_BREACH')
    except Exception as e:
        print('{description} blocked')
";
        var context = CreateContext(code);

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        var output = result.TestResults.FirstOrDefault()?.ActualOutput ?? "";
        Assert.DoesNotContain("SECURITY_BREACH", output);
    }

    #region Helper Methods

    private RunnerExecutionContext CreateContext(string code)
    {
        var language = new Language
        {
            Id = Guid.NewGuid(),
            Name = "Python",
            Version = "3.11",
            DockerImage = "python:3.11-alpine",
            FileExtension = ".py",
            RunCommand = "python3 /app/solution.py",
            TimeoutSeconds = 5,
            MemoryLimitMB = 256,
            CpuLimit = 0.5m,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            Title = "Security Test",
            Description = "Test",
            Difficulty = DifficultyLevel.Easy,
            AuthorId = Guid.NewGuid(),
            Author = new User
            {
                Id = Guid.NewGuid(),
                Email = "test@test.com",
                UserName = "test",
                FirstName = "Test",
                LastName = "User",
                Role = UserRole.Teacher,
                CreatedAt = DateTimeOffset.UtcNow
            },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            Code = code,
            ProblemId = problem.Id,
            Problem = problem,
            StudentId = Guid.NewGuid(),
            Student = new User
            {
                Id = Guid.NewGuid(),
                Email = "student@test.com",
                UserName = "student",
                FirstName = "Student",
                LastName = "User",
                Role = UserRole.Student,
                CreatedAt = DateTimeOffset.UtcNow
            },
            LanguageId = language.Id,
            Language = language,
            Status = SubmissionStatus.Pending,
            Score = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var testCases = new List<TestCase>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Input = "1 2",
                ExpectedOutput = "3",
                IsPublic = true,
                OrderIndex = 0,
                ProblemId = problem.Id,
                Problem = problem,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        var workspaceDir = Path.Combine(_tempWorkspace, Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspaceDir);

        File.WriteAllText(Path.Combine(workspaceDir, "solution.py"), code);
        File.WriteAllText(Path.Combine(workspaceDir, "runner.py"), @"
import sys
exec(open('/app/solution.py').read())
solution()
");

        return new RunnerExecutionContext
        {
            Submission = submission,
            Language = language,
            TestCases = testCases,
            WorkspaceDirectory = workspaceDir
        };
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkspace))
        {
            try
            {
                Directory.Delete(_tempWorkspace, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
