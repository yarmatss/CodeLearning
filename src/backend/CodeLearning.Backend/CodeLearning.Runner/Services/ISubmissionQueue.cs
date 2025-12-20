namespace CodeLearning.Runner.Services;

public interface ISubmissionQueue
{
    Task EnqueueAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<Guid?> DequeueAsync(CancellationToken cancellationToken = default);
    Task<long> GetQueueLengthAsync(CancellationToken cancellationToken = default);
}
