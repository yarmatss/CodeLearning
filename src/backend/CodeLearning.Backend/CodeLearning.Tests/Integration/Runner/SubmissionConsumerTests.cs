extern alias Runner;

using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using ExecutionResult = CodeLearning.Core.Models.ExecutionResult;
using TestCaseResult = CodeLearning.Core.Models.TestCaseResult;
using Npgsql;
using CodeLearning.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Runner::CodeLearning.Runner.Consumers;
using Runner::CodeLearning.Runner.Services;

namespace CodeLearning.Tests.Integration.Runner;

public class SubmissionConsumerTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private const int MaxDeliveryAttempts = 3;
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Consume_SuccessfulExecution_ShouldStoreResult()
    {
        // Arrange
        var executor = new FakeCodeExecutor(_ => new ExecutionResult { Status = SubmissionStatus.Completed, Score = 100 });
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();

        var submission = await GetSubmissionAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.Completed);
        submission.Score.Should().Be(100);
        executor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Consume_ExecutionKeepsFailing_ShouldFailSubmissionAfterMaxAttempts()
    {
        // Arrange
        var executor = new FakeCodeExecutor(_ => throw new InvalidOperationException("Docker is unavailable"));
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionFaultConsumer>().Consumed.Any<Fault<SubmissionCreated>>()).Should().BeTrue();

        var submission = await GetSubmissionAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.RuntimeError);
        submission.RuntimeError.Should().Contain("internal error");
        executor.Calls.Should().Be(MaxDeliveryAttempts);
    }

    [Fact]
    public async Task Consume_AlreadyGradedSubmission_ShouldSkipExecution()
    {
        // Arrange
        var executor = new FakeCodeExecutor(_ => new ExecutionResult { Status = SubmissionStatus.Completed, Score = 100 });
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync(SubmissionStatus.CompilationError);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();

        executor.Calls.Should().Be(0);
        (await GetSubmissionAsync(submissionId)).Status.Should().Be(SubmissionStatus.CompilationError);
    }

    [Fact]
    public async Task Consume_DuplicateMessage_ShouldExecuteOnceAndStoreASingleSetOfResults()
    {
        // Arrange
        var executor = new FakeCodeExecutor(AllPassed);
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync(testCaseCount: 2);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        await WaitUntilAsync(() =>
            Task.FromResult(harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Select<SubmissionCreated>().Count() >= 2));

        executor.Calls.Should().Be(1);
        (await GetSubmissionAsync(submissionId)).Status.Should().Be(SubmissionStatus.Completed);
        (await GetTestResultsAsync(submissionId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Consume_ResultsLeftByAnEarlierAttempt_ShouldBeReplacedNotDuplicated()
    {
        // Arrange - an earlier attempt stored a result for the same test case
        var executor = new FakeCodeExecutor(AllPassed);
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();
        await SeedStaleTestResultAsync(submissionId);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        await WaitForFinalStatusAsync(submissionId);
        var results = await GetTestResultsAsync(submissionId);
        results.Should().ContainSingle().Which.Status.Should().Be(TestResultStatus.Passed);
    }

    [Fact]
    public async Task Consume_TransientFailureThenSuccess_ShouldResumeTheRunningSubmission()
    {
        // Arrange
        var attempts = 0;
        var executor = new FakeCodeExecutor(testCases =>
            Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("Docker hiccup")
                : AllPassed(testCases));
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        var submission = await WaitForFinalStatusAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.Completed);
        submission.Score.Should().Be(100);
        executor.Calls.Should().Be(2);
        (await GetTestResultsAsync(submissionId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_OutputWithNulCharacters_ShouldBeStoredWithoutThem()
    {
        // Arrange - PostgreSQL text rejects U+0000
        var executor = new FakeCodeExecutor(testCases => new ExecutionResult
        {
            Status = SubmissionStatus.RuntimeError,
            RuntimeError = "bad\0error",
            TestResults = testCases.Select(tc => new TestCaseResult
            {
                TestCaseId = tc.Id,
                Status = TestResultStatus.RuntimeError,
                ActualOutput = "out\0put",
                ErrorMessage = "err\0or"
            }).ToList()
        });
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        var submission = await WaitForFinalStatusAsync(submissionId);
        submission.RuntimeError.Should().Be("baderror");
        var result = (await GetTestResultsAsync(submissionId)).Single();
        result.ActualOutput.Should().Be("output");
        result.ErrorMessage.Should().Be("error");
        executor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Consume_ProblemWithoutTestCases_ShouldFailWithAClearMessage()
    {
        // Arrange
        var executor = new FakeCodeExecutor(AllPassed);
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync(testCaseCount: 0);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        var submission = await WaitForFinalStatusAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.RuntimeError);
        submission.RuntimeError.Should().Contain("no test cases");
        executor.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Consume_DataErrorFromTheDatabase_ShouldNotBeRetried()
    {
        // Arrange
        var executor = new FakeCodeExecutor(_ =>
            throw new InvalidOperationException("save failed",
                new PostgresException("invalid byte sequence", "ERROR", "ERROR", PostgresErrorCodes.CharacterNotInRepertoire)));
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionFaultConsumer>().Consumed.Any<Fault<SubmissionCreated>>()).Should().BeTrue();
        (await WaitForFinalStatusAsync(submissionId)).Status.Should().Be(SubmissionStatus.RuntimeError);
        executor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Consume_SubmissionFailedBySweeperWhileExecuting_ShouldKeepTheSweeperVerdict()
    {
        // Arrange - while the code runs, the sweeper gives up on the submission
        Guid submissionId = default;
        var executor = new FakeCodeExecutor(testCases =>
        {
            using var dbContext = fixture.CreateDbContext();
            dbContext.Submissions
                .Where(s => s.Id == submissionId)
                .ExecuteUpdate(s => s.SetProperty(x => x.Status, SubmissionStatus.RuntimeError));
            return AllPassed(testCases);
        });
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        submissionId = await SeedSubmissionAsync();

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();
        executor.Calls.Should().Be(1);
        var submission = await GetSubmissionAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.RuntimeError);
        submission.Score.Should().Be(0);
        (await GetTestResultsAsync(submissionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_AcceptedSolution_ShouldCompleteTheProblemBlocksOfEnrolledCourses()
    {
        // Arrange
        var executor = new FakeCodeExecutor(AllPassed);
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();
        var (blockId, nextBlockId, studentId) = await SeedEnrolledProblemBlockAsync(submissionId);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();
        (await GetSubmissionAsync(submissionId)).Status.Should().Be(SubmissionStatus.Completed);

        await using var dbContext = fixture.CreateDbContext();
        var progress = await dbContext.StudentBlockProgresses.AsNoTracking()
            .SingleAsync(bp => bp.StudentId == studentId && bp.BlockId == blockId);
        progress.IsCompleted.Should().BeTrue();
        var enrollment = await dbContext.StudentCourseProgresses.AsNoTracking().SingleAsync(p => p.StudentId == studentId);
        enrollment.CurrentBlockId.Should().Be(nextBlockId);
    }

    [Fact]
    public async Task Consume_PartiallyCorrectSolution_ShouldNotCompleteTheProblemBlock()
    {
        // Arrange
        var executor = new FakeCodeExecutor(_ => new ExecutionResult { Status = SubmissionStatus.Completed, Score = 50 });
        await using var provider = CreateProvider(executor);
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();
        var (_, _, studentId) = await SeedEnrolledProblemBlockAsync(submissionId);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();
        (await GetSubmissionAsync(submissionId)).Score.Should().Be(50);

        await using var dbContext = fixture.CreateDbContext();
        (await dbContext.StudentBlockProgresses.AnyAsync(bp => bp.StudentId == studentId)).Should().BeFalse();
    }

    [Fact]
    public async Task Consume_ProgressWriteFails_ShouldKeepTheResultWithoutRerunningTheCode()
    {
        // Arrange - every save that touches block progress fails
        var executor = new FakeCodeExecutor(AllPassed);
        await using var provider = CreateProvider(executor, () => fixture.CreateDbContext(new FailProgressSaveInterceptor()));
        var harness = await StartHarnessAsync(provider);
        var submissionId = await SeedSubmissionAsync();
        var (_, _, studentId) = await SeedEnrolledProblemBlockAsync(submissionId);

        // Act
        await harness.Bus.Publish(new SubmissionCreated(submissionId));

        // Assert
        (await harness.GetConsumerHarness<SubmissionCreatedConsumer>().Consumed.Any<SubmissionCreated>()).Should().BeTrue();
        var submission = await GetSubmissionAsync(submissionId);
        submission.Status.Should().Be(SubmissionStatus.Completed);
        submission.Score.Should().Be(100);
        (await GetTestResultsAsync(submissionId)).Should().ContainSingle();
        executor.Calls.Should().Be(1);

        await using var dbContext = fixture.CreateDbContext();
        (await dbContext.StudentBlockProgresses.AnyAsync(bp => bp.StudentId == studentId)).Should().BeFalse();
    }

    private static ExecutionResult AllPassed(List<TestCase> testCases) => new()
    {
        Status = SubmissionStatus.Completed,
        Score = 100,
        TotalTests = testCases.Count,
        PassedTests = testCases.Count,
        TestResults = testCases.Select((tc, index) => new TestCaseResult
        {
            TestCaseId = tc.Id,
            TestIndex = index,
            Status = TestResultStatus.Passed,
            ActualOutput = tc.ExpectedOutput
        }).ToList()
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time");
            }

            await Task.Delay(100);
        }
    }

    private async Task<Submission> WaitForFinalStatusAsync(Guid submissionId)
    {
        Submission? submission = null;
        await WaitUntilAsync(async () =>
        {
            submission = await GetSubmissionAsync(submissionId);
            return submission.Status is not (SubmissionStatus.Pending or SubmissionStatus.Running);
        });
        return submission!;
    }

    private async Task SeedStaleTestResultAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        var testCaseId = await dbContext.Submissions
            .Where(s => s.Id == submissionId)
            .SelectMany(s => s.Problem.TestCases)
            .Select(tc => tc.Id)
            .SingleAsync();

        dbContext.SubmissionTestResults.Add(new SubmissionTestResult
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            Submission = null!,
            TestCaseId = testCaseId,
            TestCase = null!,
            Status = TestResultStatus.Failed,
            ActualOutput = "stale"
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task<List<SubmissionTestResult>> GetTestResultsAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.SubmissionTestResults.AsNoTracking().Where(r => r.SubmissionId == submissionId).ToListAsync();
    }

    private ServiceProvider CreateProvider(ICodeExecutor executor, Func<ApplicationDbContext>? createDbContext = null)
    {
        createDbContext ??= () => fixture.CreateDbContext();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:MaxConcurrentExecutions"] = "1",
                ["ExecutionSettings:MaxDeliveryAttempts"] = MaxDeliveryAttempts.ToString(),
                ["ExecutionSettings:RetryDelaySeconds"] = "0"
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddScoped(_ => createDbContext())
            .AddSingleton(executor)
            .AddMassTransitTestHarness(x =>
            {
                // The first consume pays for EF Core start-up, which can outlast the default inactivity window under a parallel test run
                x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(30), testInactivityTimeout: TimeSpan.FromSeconds(10));

                // The real definition, so retries follow the configured attempts
                x.AddConsumer<SubmissionCreatedConsumer, SubmissionCreatedConsumerDefinition>();
                x.AddConsumer<SubmissionFaultConsumer>();
            })
            .BuildServiceProvider(true);
    }

    private static async Task<ITestHarness> StartHarnessAsync(IServiceProvider provider)
    {
        var harness = provider.GetTestHarness();
        await harness.Start();
        return harness;
    }

    private async Task<Guid> SeedSubmissionAsync(SubmissionStatus status = SubmissionStatus.Pending, int testCaseCount = 1)
    {
        await using var dbContext = fixture.CreateDbContext();

        var student = TestDataBuilder.CreateValidUser();
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));
        for (var i = 1; i <= testCaseCount; i++)
        {
            problem.TestCases.Add(TestDataBuilder.CreateValidTestCase(problem, orderIndex: i));
        }

        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ProblemId = problem.Id,
            Problem = problem,
            StudentId = student.Id,
            Student = student,
            LanguageId = PythonLanguageId,
            Language = await dbContext.Languages.SingleAsync(l => l.Id == PythonLanguageId),
            Code = "print(1)",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.Submissions.Add(submission);
        await dbContext.SaveChangesAsync();

        return submission.Id;
    }

    // A course whose first block uses the submission's problem, followed by a theory block, with the
    // submission's student enrolled. Returns the problem block, the block after it and the student
    private async Task<(Guid BlockId, Guid NextBlockId, Guid StudentId)> SeedEnrolledProblemBlockAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        var submission = await dbContext.Submissions
            .Include(s => s.Student)
            .Include(s => s.Problem)
            .SingleAsync(s => s.Id == submissionId);

        var (course, chapter, subchapter) = TestDataBuilder.CreateCourseHierarchy(TestDataBuilder.CreateValidUser(UserRole.Teacher));
        chapter.Subchapters.Add(subchapter);
        course.Chapters.Add(chapter);

        var problemBlock = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Problem",
            Type = BlockType.Problem,
            OrderIndex = 1,
            SubchapterId = subchapter.Id,
            Subchapter = subchapter,
            ProblemId = submission.ProblemId,
            Problem = submission.Problem,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var theoryBlock = new CourseBlock
        {
            Id = Guid.NewGuid(),
            Title = "Theory",
            Type = BlockType.Theory,
            OrderIndex = 2,
            SubchapterId = subchapter.Id,
            Subchapter = subchapter,
            TheoryContent = new TheoryContent { Id = Guid.NewGuid(), Content = "Read me", Block = null! },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        subchapter.Blocks.Add(problemBlock);
        subchapter.Blocks.Add(theoryBlock);

        dbContext.Courses.Add(course);
        dbContext.StudentCourseProgresses.Add(new StudentCourseProgress
        {
            Id = Guid.NewGuid(),
            StudentId = submission.StudentId,
            Student = submission.Student,
            CourseId = course.Id,
            Course = course,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            CurrentBlockId = problemBlock.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return (problemBlock.Id, theoryBlock.Id, submission.StudentId);
    }

    private sealed class FailProgressSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<StudentBlockProgress>().Any())
            {
                throw new InvalidOperationException("Simulated progress write failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<Submission> GetSubmissionAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Submissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);
    }

    private sealed class FakeCodeExecutor(Func<List<TestCase>, ExecutionResult> execute) : ICodeExecutor
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ExecutionResult> ExecuteAsync(
            Submission submission,
            Language language,
            List<TestCase> testCases,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(execute(testCases));
        }
    }
}
