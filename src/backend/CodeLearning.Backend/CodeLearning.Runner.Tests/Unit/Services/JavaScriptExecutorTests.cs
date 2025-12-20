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

public class JavaScriptExecutorTests
{
    private readonly Mock<IDockerRunner> _dockerRunnerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<JavaScriptExecutor>> _loggerMock;
    private readonly JavaScriptExecutor _executor;

    public JavaScriptExecutorTests()
    {
        _dockerRunnerMock = new Mock<IDockerRunner>();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<JavaScriptExecutor>>();

        var tempWorkspace = Path.Combine(Path.GetTempPath(), "runner-tests-js", Guid.NewGuid().ToString());
        _configurationMock.Setup(c => c["ExecutionSettings:WorkspaceBasePath"])
            .Returns(tempWorkspace);

        _executor = new JavaScriptExecutor(
            _dockerRunnerMock.Object,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ValidJavaScriptCode_ShouldExecuteSuccessfully()
    {
        // Arrange
        var submission = CreateJavaScriptSubmission(@"
function solution() {
    const [a, b] = input.read().split(' ').map(Number);
    console.log(a + b);
}
");
        var testCases = new List<TestCase>
        {
            CreateTestCase(submission.ProblemId, "5 10", "15", 0)
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
                    ActualOutput = "15",
                    ExecutionTimeMs = 50
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
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseCorrectLanguageConfiguration()
    {
        // Arrange
        var submission = CreateJavaScriptSubmission("function solution() {}");
        var testCases = new List<TestCase> { CreateTestCase(submission.ProblemId, "1", "1", 0) };

        _dockerRunnerMock.Setup(d => d.ExecuteAsync(
                It.IsAny<RunnerExecutionContext>(),
                It.IsAny<CancellationToken>()))
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
        _dockerRunnerMock.Verify(d => d.ExecuteAsync(
            It.Is<RunnerExecutionContext>(ctx =>
                ctx.Language.Name == "JavaScript" &&
                ctx.Language.DockerImage == "node:20-alpine"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Helper Methods

    private Submission CreateJavaScriptSubmission(string code)
    {
        var userId = Guid.NewGuid();
        var problemId = Guid.NewGuid();
        var languageId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
                Name = "JavaScript",
                Version = "20",
                DockerImage = "node:20-alpine",
                FileExtension = ".js",
                RunCommand = "node /app/runner.js",
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
