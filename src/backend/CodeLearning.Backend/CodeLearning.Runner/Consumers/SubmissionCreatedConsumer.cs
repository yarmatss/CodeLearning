using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Core.Messages;
using ExecutionResult = CodeLearning.Core.Models.ExecutionResult;
using CodeLearning.Core.Text;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Infrastructure.Services.Learning;
using CodeLearning.Runner.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodeLearning.Runner.Consumers;

public class SubmissionCreatedConsumer(
    ApplicationDbContext dbContext,
    IEnumerable<ICodeExecutor> executors,
    ILogger<SubmissionCreatedConsumer> logger) : IConsumer<SubmissionCreated>
{
    internal const string InternalErrorMessage = "Execution failed due to an internal error. Please submit your solution again.";
    internal const string NoTestCasesMessage = "This problem has no test cases yet, so the submission could not be graded.";

    public async Task Consume(ConsumeContext<SubmissionCreated> context)
    {
        var submissionId = context.Message.SubmissionId;
        var cancellationToken = context.CancellationToken;

        logger.LogInformation("Processing submission {SubmissionId}", submissionId);

        // A retry of this same message, or a redelivery after the Runner died mid-execution, takes over
        // a Running submission. Any other copy of the message only gets a Pending one, so a duplicate
        // delivery never executes or grades a submission twice
        var resuming = context.GetRetryAttempt() > 0 || context.ReceiveContext.Redelivered;

        if (!await TryClaimAsync(dbContext, submissionId, resuming, cancellationToken))
        {
            logger.LogInformation(
                "Submission {SubmissionId} is missing, already claimed or already graded, skipping",
                submissionId);
            return;
        }

        var submission = await dbContext.Submissions
            .AsNoTracking()
            .Include(s => s.Language)
            .Include(s => s.Problem.TestCases.OrderBy(tc => tc.OrderIndex))
            .SingleAsync(s => s.Id == submissionId, cancellationToken);

        if (submission.Problem.TestCases.Count == 0)
        {
            logger.LogWarning("Problem {ProblemId} has no test cases", submission.ProblemId);
            await FailAsync(dbContext, submissionId, NoTestCasesMessage, cancellationToken);
            return;
        }

        var executor = executors.Single();

        // Failures propagate so the message is retried; the submission keeps its Running status
        // until an attempt succeeds or SubmissionFaultConsumer records the failure
        var result = await executor.ExecuteAsync(
            submission,
            submission.Language,
            submission.Problem.TestCases.ToList(),
            cancellationToken);

        if (!await SaveExecutionResultsAsync(dbContext, submissionId, result, logger, cancellationToken))
        {
            logger.LogWarning(
                "Submission {SubmissionId} was finished elsewhere while executing, discarding the result",
                submissionId);
            return;
        }

        logger.LogInformation(
            "Submission {SubmissionId} completed with status {Status}, score {Score}%",
            submissionId, result.Status, result.Score);
    }

    internal static async Task<bool> TryClaimAsync(
        ApplicationDbContext dbContext,
        Guid submissionId,
        bool resuming,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Switching to Running refreshes UpdatedAt, which starts the sweeper's Running timeout
        var claimed = await dbContext.Submissions
            .Where(s => s.Id == submissionId
                        && (s.Status == SubmissionStatus.Pending || (resuming && s.Status == SubmissionStatus.Running)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubmissionStatus.Running)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        return claimed == 1;
    }

    // Fails a submission that is still awaiting its result; one finished meanwhile keeps its result
    internal static async Task<bool> FailAsync(
        ApplicationDbContext dbContext,
        Guid submissionId,
        string message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var updated = await dbContext.Submissions
            .Where(s => s.Id == submissionId
                        && (s.Status == SubmissionStatus.Pending || s.Status == SubmissionStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SubmissionStatus.RuntimeError)
                .SetProperty(x => x.Score, 0)
                .SetProperty(x => x.RuntimeError, message)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        return updated == 1;
    }

    // Returns false when the submission is no longer Running (e.g. the sweeper failed it), in which case nothing is written.
    // An accepted solution (Completed, score 100) also completes the problem's blocks in the same transaction
    internal static async Task<bool> SaveExecutionResultsAsync(
        ApplicationDbContext dbContext,
        Guid submissionId,
        ExecutionResult result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var compilationError = TextSanitizer.ForDatabase(result.CompilationError);
        var runtimeError = TextSanitizer.ForDatabase(result.RuntimeError);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updated = await dbContext.Submissions
            .Where(s => s.Id == submissionId && s.Status == SubmissionStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, result.Status)
                .SetProperty(x => x.Score, result.Score)
                .SetProperty(x => x.ExecutionTimeMs, result.TotalExecutionTimeMs)
                .SetProperty(x => x.MemoryUsedKB, result.MaxMemoryUsedKB)
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.CompilationError, compilationError)
                .SetProperty(x => x.RuntimeError, runtimeError)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // An earlier attempt may have stored results before failing; one set per submission
        await dbContext.SubmissionTestResults
            .Where(r => r.SubmissionId == submissionId)
            .ExecuteDeleteAsync(cancellationToken);

        // A test case deleted while the submission ran has nothing left to attach its result to
        var reportedIds = result.TestResults.Select(tr => tr.TestCaseId).Distinct().ToList();
        var existingIds = (await dbContext.TestCases
                .Where(tc => reportedIds.Contains(tc.Id))
                .Select(tc => tc.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var testResults = result.TestResults
            .Where(tr => existingIds.Contains(tr.TestCaseId))
            .DistinctBy(tr => tr.TestCaseId)
            .Select(tr => new SubmissionTestResult
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                Submission = null!,
                TestCaseId = tr.TestCaseId,
                TestCase = null!,
                Status = tr.Status,
                ActualOutput = TextSanitizer.ForDatabase(tr.ActualOutput),
                ErrorMessage = TextSanitizer.ForDatabase(tr.ErrorMessage),
                ExecutionTimeMs = tr.ExecutionTimeMs,
                MemoryUsedKB = tr.MemoryUsedKB
            });

        dbContext.SubmissionTestResults.AddRange(testResults);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();

        if (result.Status == SubmissionStatus.Completed && result.Score == 100)
        {
            await CompleteSolvedProblemBlocksAsync(dbContext, transaction, submissionId, logger, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private const string ProgressSavepoint = "problem_progress";

    // Progress is secondary to the verdict: a failure here rolls back to the savepoint and is logged, so the
    // result still commits and the student's code is never run again because of a progress hiccup
    private static async Task CompleteSolvedProblemBlocksAsync(
        ApplicationDbContext dbContext,
        IDbContextTransaction transaction,
        Guid submissionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await transaction.CreateSavepointAsync(ProgressSavepoint, cancellationToken);

        try
        {
            var submission = await dbContext.Submissions
                .Where(s => s.Id == submissionId)
                .Select(s => new { s.StudentId, s.ProblemId })
                .SingleAsync(cancellationToken);

            var completed = await ProgressWriter.CompleteSolvedProblemBlocksAsync(
                dbContext, submission.StudentId, submission.ProblemId, cancellationToken);

            if (completed > 0 && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Submission {SubmissionId} completed {BlockCount} problem block(s)", submissionId, completed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Could not record problem progress for submission {SubmissionId}; the result is kept", submissionId);
            await transaction.RollbackToSavepointAsync(ProgressSavepoint, cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }
}
