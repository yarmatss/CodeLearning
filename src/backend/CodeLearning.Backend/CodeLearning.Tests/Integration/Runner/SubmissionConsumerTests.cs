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
using Microsoft.EntityFrameworkCore;
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
        var executor = new FakeCodeExecutor(() => new ExecutionResult { Status = SubmissionStatus.Completed, Score = 100 });
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
        var executor = new FakeCodeExecutor(() => throw new InvalidOperationException("Docker is unavailable"));
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
        var executor = new FakeCodeExecutor(() => new ExecutionResult { Status = SubmissionStatus.Completed, Score = 100 });
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

    private ServiceProvider CreateProvider(ICodeExecutor executor)
    {
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
            .AddScoped(_ => fixture.CreateDbContext())
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

    private async Task<Guid> SeedSubmissionAsync(SubmissionStatus status = SubmissionStatus.Pending)
    {
        await using var dbContext = fixture.CreateDbContext();

        var student = TestDataBuilder.CreateValidUser();
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));
        problem.TestCases.Add(TestDataBuilder.CreateValidTestCase(problem));

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

    private async Task<Submission> GetSubmissionAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Submissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);
    }

    private sealed class FakeCodeExecutor(Func<ExecutionResult> execute) : ICodeExecutor
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
            return Task.FromResult(execute());
        }
    }
}
