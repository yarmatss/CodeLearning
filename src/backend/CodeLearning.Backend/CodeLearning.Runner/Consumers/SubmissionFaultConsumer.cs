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

        // Guarded, so a submission that was graded meanwhile keeps its result
        if (await SubmissionCreatedConsumer.FailAsync(
                dbContext,
                submissionId,
                SubmissionCreatedConsumer.InternalErrorMessage,
                context.CancellationToken))
        {
            logger.LogError("Gave up on submission {SubmissionId} after its execution attempts failed", submissionId);

            var studentId = await dbContext.Submissions
                .Where(s => s.Id == submissionId)
                .Select(s => s.StudentId)
                .SingleAsync(context.CancellationToken);

            await SubmissionStatusEvents.PublishAsync(
                context, logger, submissionId, studentId, SubmissionStatus.RuntimeError, context.CancellationToken);
        }
    }
}
