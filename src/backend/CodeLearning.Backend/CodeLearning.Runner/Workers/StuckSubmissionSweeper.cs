using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Consumers;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Runner.Workers;

// Safety net for submissions no message will ever finish, e.g. one whose message the broker dropped
// after repeated Runner crashes
public class StuckSubmissionSweeper(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StuckSubmissionSweeper> logger) : BackgroundService
{
    // Running: about twice the worst case of a real execution (every attempt hitting the container cap,
    // plus the retry delays), measured from the switch to Running, which refreshes UpdatedAt
    private readonly TimeSpan _runningTimeout = TimeSpan.FromMinutes(
        configuration.GetValue("ExecutionSettings:StuckSubmissionTimeoutMinutes", 30));

    // Pending: the message is still in the durable queue, so a backlog or a Runner outage must not
    // fail it; only a submission waiting far longer than any realistic backlog is given up on
    private readonly TimeSpan _pendingTimeout = TimeSpan.FromHours(
        configuration.GetValue("ExecutionSettings:PendingSubmissionTimeoutHours", 24));

    private readonly TimeSpan _interval = TimeSpan.FromSeconds(
        configuration.GetValue("ExecutionSettings:StuckSubmissionSweepSeconds", 300));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        // The first sweep waits one interval, so a Runner coming back up first resumes the queued
        // and redelivered messages instead of racing them
        while (await timer.WaitForNextTickAsync(stoppingToken))
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
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTimeOffset.UtcNow;
        var runningCutoff = now - _runningTimeout;
        var pendingCutoff = now - _pendingTimeout;

        // One guarded UPDATE: the status and age are checked again on each row as it is written, so a submission
        // the consumer finishes (or claims, refreshing UpdatedAt) in the meantime is left alone
        var failed = await dbContext.Submissions
            .Where(s => (s.Status == SubmissionStatus.Running && s.UpdatedAt < runningCutoff)
                     || (s.Status == SubmissionStatus.Pending && s.UpdatedAt < pendingCutoff))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubmissionStatus.RuntimeError)
                .SetProperty(x => x.Score, 0)
                .SetProperty(x => x.RuntimeError, SubmissionCreatedConsumer.InternalErrorMessage)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        if (failed > 0)
        {
            logger.LogWarning("Failed {Count} submissions that made no progress in time", failed);
        }

        return failed;
    }
}
