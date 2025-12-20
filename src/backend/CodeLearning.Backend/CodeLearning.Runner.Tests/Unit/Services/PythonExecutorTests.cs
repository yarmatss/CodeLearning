using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Runner.Models;
using CodeLearning.Runner.Services;
using CodeLearning.Runner.Services.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RunnerExecutionContext = CodeLearning.Runner.Models.ExecutionContext;

namespace CodeLearning.Runner.Tests.Unit.Services;

public class PythonExecutorTests
{
    private readonly Mock<IDockerRunner> _dockerRunnerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<PythonExecutor>> _loggerMock;
    private readonly PythonExecutor _executor;
    private readonly string _tempWorkspace;

    public PythonExecutorTests()
    {
        _dockerRunnerMock = new Mock<IDockerRunner>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<PythonExecutor>>();

        _tempWorkspace = Path.Combine(Path.GetTempPath(), "runner-tests", Guid.NewGuid().ToString());
        _configurationMock.Setup(c => c["ExecutionSettings:WorkspaceBasePath"])
            .Returns(_tempWorkspace);

        _executor = new PythonExecutor(
            _dockerRunnerMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ValidCode_ShouldPrepareWorkspaceAndCallDockerRunner()
    {
        // Arrange
        var submission = CreateTestSubmission("def solution():\n    print('Hello')");
        var testCases = new List<TestCase>
        {
            CreateTestCase(submission.ProblemId, "1 2", "3", 0)
        };

        var expectedResult = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            Score = 100,
            TotalTests = 1,
            PassedTests = 1,
            TestResults = new List<TestCaseResult>
            {
                new()
                {
                    TestCaseId = testCases[0].Id,
                    Status = TestResultStatus.Passed,
                    ActualOutput = "3",
                    ExecutionTimeMs = 100
                }
            }
        };

        _dockerRunnerMock.Setup(d => d.ExecuteAsync(
                It.IsAny<RunnerExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _executor.ExecuteAsync(
            submission,
            submission.Language,
            testCases,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(SubmissionStatus.Completed, result.Status);
        Assert.Equal(100, result.Score);
        Assert.Equal(1, result.PassedTests);
        Assert.Equal(1, result.TotalTests);

        _dockerRunnerMock.Verify(d => d.ExecuteAsync(
            It.Is<RunnerExecutionContext>(ctx =>
                ctx.Submission.Id == submission.Id &&
                ctx.Language.Id == submission.LanguageId &&
                ctx.TestCases.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleTestCases_ShouldIncludeAllInContext()
    {
        // Arrange
        var submission = CreateTestSubmission("def solution():\n    pass");
        var testCases = new List<TestCase>
        {
            CreateTestCase(submission.ProblemId, "1 2", "3", 0),
            CreateTestCase(submission.ProblemId, "5 10", "15", 1),
            CreateTestCase(submission.ProblemId, "100 200", "300", 2)
        };

        var expectedResult = new ExecutionResult
        {
            Status = SubmissionStatus.Completed,
            Score = 100,
            TotalTests = 3,
            PassedTests = 3,
            TestResults = testCases.Select(tc => new TestCaseResult
            {
                TestCaseId = tc.Id,
                Status = TestResultStatus.Passed
            }).ToList()
        };

        _dockerRunnerMock.Setup(d => d.ExecuteAsync(
                It.IsAny<RunnerExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _executor.ExecuteAsync(
            submission,
            submission.Language,
            testCases,
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.TotalTests);
        Assert.Equal(3, result.TestResults.Count);

        _dockerRunnerMock.Verify(d => d.ExecuteAsync(
            It.Is<RunnerExecutionContext>(ctx => ctx.TestCases.Count == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCleanupWorkspaceAfterExecution()
    {
        // Arrange
        var submission = CreateTestSubmission("def solution(): pass");
        var testCases = new List<TestCase> { CreateTestCase(submission.ProblemId, "1", "1", 0) };

        string? capturedWorkspace = null;
        _dockerRunnerMock.Setup(d => d.ExecuteAsync(
                It.IsAny<RunnerExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<RunnerExecutionContext, CancellationToken>((ctx, ct) =>
            {
                capturedWorkspace = ctx.WorkspaceDirectory;
            })
            .ReturnsAsync(new ExecutionResult
            {
                Status = SubmissionStatus.Completed,
                Score = 100,
                TotalTests = 1,
                PassedTests = 1,
                TestResults = new List<TestCaseResult>()
            });

        // Act
        await _executor.ExecuteAsync(submission, submission.Language, testCases, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedWorkspace);
        Assert.False(Directory.Exists(capturedWorkspace), "Workspace should be cleaned up");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDockerRunnerThrows_ShouldStillCleanupWorkspace()
    {
        // Arrange
        var submission = CreateTestSubmission("def solution(): pass");
        var testCases = new List<TestCase> { CreateTestCase(submission.ProblemId, "1", "1", 0) };

        string? capturedWorkspace = null;
        _dockerRunnerMock.Setup(d => d.ExecuteAsync(
                It.IsAny<RunnerExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<RunnerExecutionContext, CancellationToken>((ctx, ct) =>
            {
                capturedWorkspace = ctx.WorkspaceDirectory;
            })
            .ThrowsAsync(new Exception("Docker error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () => 
            await _executor.ExecuteAsync(submission, submission.Language, testCases, CancellationToken.None));
        
        Assert.NotNull(capturedWorkspace);
        Assert.False(Directory.Exists(capturedWorkspace), "Workspace should be cleaned up even on error");
    }

    #region Helper Methods

    private Submission CreateTestSubmission(string code)
    {
        var userId = Guid.NewGuid();
        var problemId = Guid.NewGuid();
        var languageId = Guid.NewGuid();

        return new Submission
        {
            Id = Guid.NewGuid(),
            Code = code,
            ProblemId = problemId,
            Problem = new Problem
            {
                Id = problemId,
                Title = "Test Problem",
                Description = "Test",
                Difficulty = DifficultyLevel.Easy,
                AuthorId = userId,
                Author = new User
                {
                    Id = userId,
                    Email = "test@test.com",
                    UserName = "test",
                    FirstName = "Test",
                    LastName = "User",
                    Role = UserRole.Teacher,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
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
            LanguageId = languageId,
            Language = new Language
            {
                Id = languageId,
                Name = "Python",
                Version = "3.11",
                DockerImage = "python:3.11-alpine",
                FileExtension = ".py",
                RunCommand = "python3 /app/runner.py",
                TimeoutSeconds = 5,
                MemoryLimitMB = 256,
                CpuLimit = 0.5m,
                IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            Status = SubmissionStatus.Pending,
            Score = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private TestCase CreateTestCase(Guid problemId, string input, string expectedOutput, int orderIndex)
    {
        return new TestCase
        {
            Id = Guid.NewGuid(),
            Input = input,
            ExpectedOutput = expectedOutput,
            IsPublic = true,
            OrderIndex = orderIndex,
            ProblemId = problemId,
            Problem = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
