using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CodeLearning.Runner.Consumers;

public class SubmissionCreatedConsumer(
    ApplicationDbContext dbContext,
    IEnumerable<ICodeExecutor> executors,
    ILogger<SubmissionCreatedConsumer> logger) : IConsumer<SubmissionCreated>
{
    public async Task Consume(ConsumeContext<SubmissionCreated> context)
    {
        var submissionId = context.Message.SubmissionId;
        var cancellationToken = context.CancellationToken;

        logger.LogInformation("Processing submission {SubmissionId}", submissionId);

        var submission = await dbContext.Submissions
            .Include(s => s.Language)
            .Include(s => s.Problem.TestCases.OrderBy(tc => tc.OrderIndex))
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission == null)
        {
            logger.LogWarning("Submission {SubmissionId} not found in DB", submissionId);
            return;
        }

        // A redelivered message may belong to a submission that was graded before its acknowledgement got through
        if (!IsAwaitingResult(submission))
        {
            logger.LogInformation(
                "Submission {SubmissionId} already has status {Status}, skipping",
                submissionId,
                submission.Status);
            return;
        }

        if (submission.Problem.TestCases.Count == 0)
        {
            logger.LogWarning("Problem {ProblemId} has no test cases", submission.ProblemId);
            await UpdateSubmissionStatusAsync(dbContext, submission, SubmissionStatus.RuntimeError, cancellationToken);
            return;
        }

        submission.Status = SubmissionStatus.Running;
        await dbContext.SaveChangesAsync(cancellationToken);

        var executor = executors.Single();

        // Failures propagate so the message is retried; the submission keeps its Running status
        // until an attempt succeeds or SubmissionFaultConsumer records the failure
        var result = await executor.ExecuteAsync(
            submission,
            submission.Language,
            submission.Problem.TestCases.ToList(),
            cancellationToken);

        await SaveExecutionResultsAsync(dbContext, submission, result, cancellationToken);

        logger.LogInformation(
            "Submission {SubmissionId} completed with status {Status}, score {Score}%",
            submission.Id, result.Status, result.Score);
    }

    internal static bool IsAwaitingResult(Submission submission) =>
        submission.Status is SubmissionStatus.Pending or SubmissionStatus.Running;

    internal static async Task UpdateSubmissionStatusAsync(
        ApplicationDbContext dbContext,
        Submission submission,
        SubmissionStatus status,
        CancellationToken cancellationToken)
    {
        submission.Status = status;
        submission.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task SaveExecutionResultsAsync(
        ApplicationDbContext dbContext,
        Submission submission,
        Core.Models.ExecutionResult result,
        CancellationToken cancellationToken)
    {
        submission.Status = result.Status;
        submission.Score = result.Score;
        submission.ExecutionTimeMs = result.TotalExecutionTimeMs;
        submission.MemoryUsedKB = result.MaxMemoryUsedKB;
        submission.CompletedAt = DateTimeOffset.UtcNow;
        submission.CompilationError = result.CompilationError;
        submission.RuntimeError = result.RuntimeError;

        foreach (var testResult in result.TestResults)
        {
            var submissionTestResult = new SubmissionTestResult
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                Submission = submission,
                TestCaseId = testResult.TestCaseId,
                TestCase = await dbContext.TestCases.FindAsync([testResult.TestCaseId], cancellationToken)
                    ?? throw new InvalidOperationException($"TestCase {testResult.TestCaseId} not found"),
                Status = testResult.Status,
                ActualOutput = testResult.ActualOutput,
                ErrorMessage = testResult.ErrorMessage,
                ExecutionTimeMs = testResult.ExecutionTimeMs,
                MemoryUsedKB = testResult.MemoryUsedKB,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            dbContext.SubmissionTestResults.Add(submissionTestResult);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
