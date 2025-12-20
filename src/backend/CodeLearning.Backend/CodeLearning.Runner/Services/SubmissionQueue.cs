using StackExchange.Redis;

namespace CodeLearning.Runner.Services;

public class SubmissionQueue(IConnectionMultiplexer redis, ILogger<SubmissionQueue> logger) : ISubmissionQueue
{
    private const string QueueKey = "submissions:pending";

    public async Task EnqueueAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            await db.ListRightPushAsync(QueueKey, submissionId.ToString());
            
            logger.LogInformation("Enqueued submission {SubmissionId}", submissionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue submission {SubmissionId}", submissionId);
            throw;
        }
    }

    public async Task<Guid?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            var value = await db.ListLeftPopAsync(QueueKey);
            
            if (value.HasValue && Guid.TryParse((string?)value, out var submissionId))
            {
                logger.LogInformation("Dequeued submission {SubmissionId}", submissionId);
                return submissionId;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dequeue submission");
            return null;
        }
    }

    public async Task<long> GetQueueLengthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = redis.GetDatabase();
            return await db.ListLengthAsync(QueueKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get queue length");
            return 0;
        }
    }
}
