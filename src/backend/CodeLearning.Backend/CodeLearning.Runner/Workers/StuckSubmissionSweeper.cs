using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Consumers;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Runner.Workers;

// Safety net for submissions no message will ever finish: one published before the queue existed,
// or one whose message the broker dropped after repeated Runner crashes
public class StuckSubmissionSweeper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StuckSubmissionSweeper> logger) : BackgroundService
{
    // About twice the worst case of a real execution: every attempt hitting the container cap, plus the retry delays
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(
        configuration.GetValue("ExecutionSettings:StuckSubmissionTimeoutMinutes", 30));

    private readonly TimeSpan _interval = TimeSpan.FromSeconds(
        configuration.GetValue("ExecutionSettings:StuckSubmissionSweepSeconds", 300));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to sweep stuck submissions");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // UpdatedAt is refreshed when a submission switches to Running, so it measures time since the last progress
        var cutoff = DateTimeOffset.UtcNow - _timeout;
        var stuck = await dbContext.Submissions
            .Where(s => (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.Running) && s.UpdatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stuck.Count == 0)
        {
            return 0;
        }

        foreach (var submission in stuck)
        {
            submission.Status = SubmissionStatus.RuntimeError;
            submission.RuntimeError = SubmissionCreatedConsumer.InternalErrorMessage;
            submission.CompletedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Failed {Count} submissions that had no result after {Timeout}", stuck.Count, _timeout);
        return stuck.Count;
    }
}
