extern alias Runner;

using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Tests.Fixtures;
using CodeLearning.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Runner::CodeLearning.Runner.Workers;

namespace CodeLearning.Tests.Integration.Runner;

public class StuckSubmissionSweeperTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid PythonLanguageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Sweep_ShouldFailOnlySubmissionsWithoutProgressPastTheirTimeout()
    {
        // Arrange
        var staleRunning = await SeedSubmissionAsync(SubmissionStatus.Running, TimeSpan.FromHours(1));
        var abandonedPending = await SeedSubmissionAsync(SubmissionStatus.Pending, TimeSpan.FromHours(25));
        var recentRunning = await SeedSubmissionAsync(SubmissionStatus.Running, TimeSpan.FromMinutes(5));
        // Still queued (backlog or Runner outage): its message will be processed, so it must survive
        var queuedPending = await SeedSubmissionAsync(SubmissionStatus.Pending, TimeSpan.FromHours(1));
        var staleCompleted = await SeedSubmissionAsync(SubmissionStatus.Completed, TimeSpan.FromHours(30));

        var sweeper = CreateSweeper();

        // Act
        await sweeper.SweepAsync(CancellationToken.None);

        // Assert
        foreach (var id in new[] { staleRunning, abandonedPending })
        {
            var submission = await GetSubmissionAsync(id);
            submission.Status.Should().Be(SubmissionStatus.RuntimeError);
            submission.RuntimeError.Should().Contain("internal error");
            submission.CompletedAt.Should().NotBeNull();
        }

        (await GetSubmissionAsync(recentRunning)).Status.Should().Be(SubmissionStatus.Running);
        (await GetSubmissionAsync(queuedPending)).Status.Should().Be(SubmissionStatus.Pending);
        (await GetSubmissionAsync(staleCompleted)).Status.Should().Be(SubmissionStatus.Completed);
    }

    [Fact]
    public async Task SwitchingToRunning_ShouldRefreshUpdatedAt_SoTheRunningTimeoutStartsThen()
    {
        // Arrange: created (and last updated) an hour ago, e.g. waiting behind a backlog
        var submissionId = await SeedSubmissionAsync(SubmissionStatus.Pending, TimeSpan.FromHours(1));

        // Act: the consumer picks it up through a normal SaveChanges
        await using (var dbContext = fixture.CreateDbContext())
        {
            var submission = await dbContext.Submissions.SingleAsync(s => s.Id == submissionId);
            submission.Status = SubmissionStatus.Running;
            await dbContext.SaveChangesAsync();
        }

        await CreateSweeper().SweepAsync(CancellationToken.None);

        // Assert
        var stored = await GetSubmissionAsync(submissionId);
        stored.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        stored.Status.Should().Be(SubmissionStatus.Running);
    }

    private StuckSubmissionSweeper CreateSweeper()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => fixture.CreateDbContext())
            .BuildServiceProvider(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:StuckSubmissionTimeoutMinutes"] = "30",
                ["ExecutionSettings:PendingSubmissionTimeoutHours"] = "24"
            })
            .Build();

        return new StuckSubmissionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<StuckSubmissionSweeper>.Instance);
    }

    private async Task<Guid> SeedSubmissionAsync(SubmissionStatus status, TimeSpan age)
    {
        await using var dbContext = fixture.CreateDbContext();

        var student = TestDataBuilder.CreateValidUser();
        var problem = TestDataBuilder.CreateValidProblem(TestDataBuilder.CreateValidUser(UserRole.Teacher));

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
            Status = status
        };

        dbContext.Submissions.Add(submission);
        await dbContext.SaveChangesAsync();

        // SaveChanges stamps UpdatedAt with the current time, so backdate it directly
        var updatedAt = DateTimeOffset.UtcNow - age;
        await dbContext.Submissions
            .Where(s => s.Id == submission.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, updatedAt));

        return submission.Id;
    }

    private async Task<Submission> GetSubmissionAsync(Guid submissionId)
    {
        await using var dbContext = fixture.CreateDbContext();
        return await dbContext.Submissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);
    }
}
