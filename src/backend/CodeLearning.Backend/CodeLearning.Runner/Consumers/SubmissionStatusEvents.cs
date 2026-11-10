using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using MassTransit;

namespace CodeLearning.Runner.Consumers;

public static class SubmissionStatusEvents
{
    // Call only after the status is committed. The event just tells the browser of the student to look, so a
    // failure to publish is logged and swallowed: it must never fail the message and run the code again
    public static async Task PublishAsync(
        IPublishEndpoint publishEndpoint,
        ILogger logger,
        Guid submissionId,
        Guid studentId,
        SubmissionStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            await publishEndpoint.Publish(new SubmissionStatusChanged(submissionId, studentId, status), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Could not announce status {Status} of submission {SubmissionId}; the client will find it on its own",
                status, submissionId);
        }
    }
}
