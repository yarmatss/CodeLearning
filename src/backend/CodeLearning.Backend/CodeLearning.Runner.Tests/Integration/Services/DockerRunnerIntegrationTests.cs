using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Runner.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RunnerExecutionContext = CodeLearning.Runner.Models.ExecutionContext;

namespace CodeLearning.Runner.Tests.Integration.Services;

[Trait("Category", "Integration")]
[Trait("Category", "Docker")]
public class DockerRunnerIntegrationTests : IDisposable
{
    private readonly IDockerRunner _dockerRunner;
    private readonly string _tempWorkspace;

    public DockerRunnerIntegrationTests()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ExecutionSettings:DockerHost", "unix:///var/run/docker.sock" },
                { "ExecutionSettings:WorkspaceBasePath", Path.GetTempPath() }
            });

        var configuration = configBuilder.Build();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<DockerRunner>();

        _dockerRunner = new DockerRunner(configuration, logger);
        _tempWorkspace = Path.Combine(Path.GetTempPath(), "docker-integration-tests");

        if (Directory.Exists(_tempWorkspace))
        {
            Directory.Delete(_tempWorkspace, true);
        }
        Directory.CreateDirectory(_tempWorkspace);
    }

    [Fact]
    public async Task ExecuteAsync_ValidPythonCode_ShouldReturnCompletedStatus()
    {
        // Arrange
        var code = @"
def solution():
    a, b = map(int, input().split())
    print(a + b)
";
        var context = CreatePythonContext(code, new[] { ("5 10", "15") });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Single(result.TestResults);
        Assert.Equal(TestResultStatus.Passed, result.TestResults[0].Status);
        Assert.Equal("15", result.TestResults[0].ActualOutput);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleTestCases_ShouldExecuteAll()
    {
        // Arrange
        var code = @"
def solution():
    a, b = map(int, input().split())
    print(a + b)
";
        var context = CreatePythonContext(code, new[]
        {
            ("1 2", "3"),
            ("5 10", "15"),
            ("100 200", "300")
        });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Equal(3, result.TestResults.Count);
        Assert.All(result.TestResults, tr => Assert.Equal(TestResultStatus.Passed, tr.Status));
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public async Task ExecuteAsync_WrongAnswer_ShouldReturnFailedStatus()
    {
        // Arrange
        var code = @"
def solution():
    print('999')
";
        var context = CreatePythonContext(code, new[] { ("1 2", "3") });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Equal(TestResultStatus.Failed, result.TestResults[0].Status);
        Assert.Equal("999", result.TestResults[0].ActualOutput);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeError_ShouldReturnRuntimeErrorStatus()
    {
        // Arrange
        var code = @"
def solution():
    raise Exception('Test error')
";
        var context = CreatePythonContext(code, new[] { ("1 2", "3") });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.RuntimeError, result.Status);
        Assert.Equal(TestResultStatus.RuntimeError, result.TestResults[0].Status);
        Assert.Contains("Test error", result.TestResults[0].ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_SyntaxError_ShouldReturnCompilationError()
    {
        // Arrange
        var code = @"
def solution():
    invalid syntax here
";
        var context = CreatePythonContext(code, new[] { ("1 2", "3") });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.CompilationError, result.Status);
        Assert.False(string.IsNullOrEmpty(result.CompilationError));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMeasureExecutionTime()
    {
        // Arrange
        var code = @"
def solution():
    import time
    time.sleep(0.1)
    a, b = map(int, input().split())
    print(a + b)
";
        var context = CreatePythonContext(code, new[] { ("1 2", "3") });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalExecutionTimeMs > 100, $"Expected > 100ms but got {result.TotalExecutionTimeMs}ms");
        Assert.True(result.TestResults[0].ExecutionTimeMs > 100, $"Expected > 100ms but got {result.TestResults[0].ExecutionTimeMs}ms");
    }

    [Fact]
    public async Task ExecuteAsync_PartialSuccess_ShouldCalculateCorrectScore()
    {
        // Arrange
        var code = @"
def solution():
    a, b = map(int, input().split())
    if a < 50:
        print(a + b)
    else:
        print('wrong')
";
        var context = CreatePythonContext(code, new[]
        {
            ("1 2", "3"),      // Pass
            ("5 10", "15"),    // Pass
            ("100 200", "300") // Fail
        });

        // Act
        var result = await _dockerRunner.ExecuteAsync(context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Equal(2, result.PassedTests);
        Assert.Equal(3, result.TotalTests);
        Assert.Equal(66, result.Score); // 2/3 * 100 = 66
    }

    #region Helper Methods

    private RunnerExecutionContext CreatePythonContext(string code, (string input, string expectedOutput)[] testCases)
    {
        var language = new Language
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Python",
            Version = "3.11",
            DockerImage = "python:3.11-alpine",
            FileExtension = ".py",
            RunCommand = "python3 /app/runner.py",
            TimeoutSeconds = 10,
            MemoryLimitMB = 256,
            CpuLimit = 0.5m,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var problem = new Problem
        {
            Id = Guid.NewGuid(),
            Title = "Integration Test",
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

        var testCaseEntities = testCases.Select((tc, index) => new TestCase
        {
            Id = Guid.NewGuid(),
            Input = tc.input,
            ExpectedOutput = tc.expectedOutput,
            IsPublic = true,
            OrderIndex = index,
            ProblemId = problem.Id,
            Problem = problem,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();

        var workspaceDir = Path.Combine(_tempWorkspace, Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspaceDir);

        // Create wrapper that will be used by actual execution
        var wrapperScript = GeneratePythonWrapper(testCaseEntities);
        File.WriteAllText(Path.Combine(workspaceDir, "solution.py"), code);
        File.WriteAllText(Path.Combine(workspaceDir, "runner.py"), wrapperScript);

        return new RunnerExecutionContext
        {
            Submission = submission,
            Language = language,
            TestCases = testCaseEntities,
            WorkspaceDirectory = workspaceDir
        };
    }

    private string GeneratePythonWrapper(List<TestCase> testCases)
    {
        var testCasesJson = System.Text.Json.JsonSerializer.Serialize(testCases.Select(tc => new
        {
            id = tc.Id,
            input = tc.Input,
            expectedOutput = tc.ExpectedOutput
        }));

        return $@"
import sys
import json
import traceback
import time
from io import StringIO

try:
    with open('/app/solution.py', 'r') as f:
        student_code = f.read()
    exec(student_code, globals())
except Exception as e:
    print(json.dumps([{{
        ""testCaseId"": ""00000000-0000-0000-0000-000000000000"",
        ""status"": 0,
        ""errorMessage"": str(e),
        ""stackTrace"": traceback.format_exc()
    }}]))
    sys.exit(1)

tests = {testCasesJson}
results = []

for test in tests:
    start_time = time.time()
    
    try:
        sys.stdin = StringIO(test['input'])
        old_stdout = sys.stdout
        sys.stdout = buffer = StringIO()
        
        if 'solution' in globals():
            solution()
        else:
            raise NameError(""Function 'solution' not defined"")
        
        output = buffer.getvalue().strip()
        sys.stdout = old_stdout
        
        execution_time_ms = int((time.time() - start_time) * 1000)
        passed = output == test['expectedOutput']
        
        results.append({{
            ""testCaseId"": test['id'],
            ""status"": 1 if passed else 2,
            ""actualOutput"": output,
            ""executionTimeMs"": execution_time_ms
        }})
        
    except Exception as e:
        execution_time_ms = int((time.time() - start_time) * 1000)
        
        results.append({{
            ""testCaseId"": test['id'],
            ""status"": 3,
            ""errorMessage"": str(e),
            ""executionTimeMs"": execution_time_ms
        }})

print(json.dumps(results))
";
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
