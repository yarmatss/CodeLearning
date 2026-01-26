using CodeLearning.Runner.Models;

namespace CodeLearning.Runner.Services;

public interface ISubmissionQueue
{
    Task EnqueueAsync(Guid submissionId, CancellationToken cancellationToken = default);
    Task<QueueMessage?> DequeueAsync(CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(string streamId, CancellationToken cancellationToken = default);

}
