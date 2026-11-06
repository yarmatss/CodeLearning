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
    public async Task Sweep_ShouldFailOnlySubmissionsWithoutProgressPastTheTimeout()
    {
        // Arrange
        var stalePending = await SeedSubmissionAsync(SubmissionStatus.Pending, TimeSpan.FromHours(1));
        var staleRunning = await SeedSubmissionAsync(SubmissionStatus.Running, TimeSpan.FromHours(1));
        var recentPending = await SeedSubmissionAsync(SubmissionStatus.Pending, TimeSpan.FromMinutes(5));
        var staleCompleted = await SeedSubmissionAsync(SubmissionStatus.Completed, TimeSpan.FromHours(1));

        await using var provider = new ServiceCollection()
            .AddScoped(_ => fixture.CreateDbContext())
            .BuildServiceProvider(true);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:StuckSubmissionTimeoutMinutes"] = "30"
            })
            .Build();

        var sweeper = new StuckSubmissionSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<StuckSubmissionSweeper>.Instance);

        // Act
        await sweeper.SweepAsync(CancellationToken.None);

        // Assert
        foreach (var id in new[] { stalePending, staleRunning })
        {
            var submission = await GetSubmissionAsync(id);
            submission.Status.Should().Be(SubmissionStatus.RuntimeError);
            submission.RuntimeError.Should().Contain("internal error");
            submission.CompletedAt.Should().NotBeNull();
        }

        (await GetSubmissionAsync(recentPending)).Status.Should().Be(SubmissionStatus.Pending);
        (await GetSubmissionAsync(staleCompleted)).Status.Should().Be(SubmissionStatus.Completed);
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
