using CodeLearning.Core.Constants;
using CodeLearning.Runner.Models;
using StackExchange.Redis;

namespace CodeLearning.Runner.Services;

public class SubmissionQueue : ISubmissionQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SubmissionQueue> _logger;

    private readonly string _consumerName;

    public SubmissionQueue(IConnectionMultiplexer redis, ILogger<SubmissionQueue> logger)
    {
        _redis = redis;
        _logger = logger;
        _consumerName = $"runner-{Environment.MachineName}-{Guid.NewGuid().ToString()[..8]}";

        InitializeGroup();
    }

    private void InitializeGroup()
    {
        try
        {
            var db = _redis.GetDatabase();

            if (!db.KeyExists(StreamConstants.SubmissionsStreamKey) ||
                (db.StreamGroupInfo(StreamConstants.SubmissionsStreamKey)).All(x => x.Name != StreamConstants.RunnersConsumerGroup))
            {
                db.StreamCreateConsumerGroup(
                    StreamConstants.SubmissionsStreamKey,
                    StreamConstants.RunnersConsumerGroup,
                    "0-0",
                    true);

                _logger.LogInformation("Initialized Redis Stream Consumer Group: {Group}", StreamConstants.RunnersConsumerGroup);
            }
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Group already exists
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Redis Stream group");
        }
    }

    public async Task EnqueueAsync(Guid submissionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            await db.StreamAddAsync(
                StreamConstants.SubmissionsStreamKey,
                StreamConstants.SubmissionIdField,
                submissionId.ToString());

            _logger.LogInformation("Enqueued submission {SubmissionId} to stream", submissionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue submission {SubmissionId}", submissionId);
            throw;
        }
    }

    public async Task<QueueMessage?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        try
        {
            // Try to read new messages for this group
            // ">" ID means: give me messages never delivered to any consumer in this group
            var results = await db.StreamReadGroupAsync(
                StreamConstants.SubmissionsStreamKey,
                StreamConstants.RunnersConsumerGroup,
                _consumerName,
                ">",
                count: 1);

            if (results.Length > 0)
            {
                return ParseStreamEntry(results[0]);
            }

            // If no new messages, try to claim pending (stuck) messages from crashed runners
            return await ClaimPendingMessagesAsync(db);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dequeuing from stream");
            return null;
        }
    }

    public async Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            await db.StreamAcknowledgeAsync(
                StreamConstants.SubmissionsStreamKey,
                StreamConstants.RunnersConsumerGroup,
                streamId);

            _logger.LogDebug("Acknowledged message {StreamId}", streamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge message {StreamId}", streamId);
        }
    }

    private async Task<QueueMessage?> ClaimPendingMessagesAsync(IDatabase db)
    {
        // Check for messages pending for more than 60 seconds (abandoned by crashed workers)
        var minIdleTime = TimeSpan.FromSeconds(60);

        // XAUTOCLAIM: 
        // 1. Scans PEL for messages idle > minIdleTime
        // 2. Changes ownership to THIS consumer
        // 3. Returns the message
        var claimResult = await db.StreamAutoClaimAsync(
            StreamConstants.SubmissionsStreamKey,
            StreamConstants.RunnersConsumerGroup,
            _consumerName,
            (long)minIdleTime.TotalMilliseconds,
            "0-0", // Start scanning from beginning
            count: 1);

        if (claimResult.ClaimedEntries.Length > 0)
        {
            var entry = claimResult.ClaimedEntries[0];
            _logger.LogWarning("Recovered abandoned submission from stream message {StreamId}", entry.Id);
            return ParseStreamEntry(entry);
        }

        return null;
    }

    private QueueMessage? ParseStreamEntry(StreamEntry entry)
    {
        // Extract the submission ID from the Name-Value pair
        var value = entry.Values.FirstOrDefault(v => v.Name == StreamConstants.SubmissionIdField).Value;

        if (value.HasValue && Guid.TryParse(value.ToString(), out var submissionId))
        {
            return new QueueMessage(entry.Id.ToString(), submissionId);
        }

        _logger.LogWarning("Received invalid stream entry {StreamId}", entry.Id);

        AcknowledgeAsync(entry.Id.ToString()).ConfigureAwait(false);

        return null;
    }
}
