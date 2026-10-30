using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Runner.Consumers;

// Every attempt to execute the submission failed, so record a final result instead of leaving it Running
public class SubmissionFaultConsumer(
    ApplicationDbContext dbContext,
    ILogger<SubmissionFaultConsumer> logger) : IConsumer<Fault<SubmissionCreated>>
{
    public async Task Consume(ConsumeContext<Fault<SubmissionCreated>> context)
    {
        var submissionId = context.Message.Message.SubmissionId;

        var submission = await dbContext.Submissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, context.CancellationToken);

        if (submission == null || !SubmissionCreatedConsumer.IsAwaitingResult(submission))
        {
            return;
        }

        logger.LogError("Giving up on submission {SubmissionId} after its execution attempts failed", submissionId);

        submission.RuntimeError = "Execution failed due to an internal error. Please submit your solution again.";
        await SubmissionCreatedConsumer.UpdateSubmissionStatusAsync(
            dbContext, submission, SubmissionStatus.RuntimeError, context.CancellationToken);
    }
}
