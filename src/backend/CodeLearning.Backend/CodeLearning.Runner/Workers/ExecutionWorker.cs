using CodeLearning.Core.Entities;
using CodeLearning.Core.Enums;
using CodeLearning.Infrastructure.Data;
using CodeLearning.Runner.Services;
using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace CodeLearning.Runner.Workers;

public class ExecutionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExecutionWorker> _logger;
    private readonly int _pollIntervalMs;
    private readonly int _maxConcurrentExecutions;

    public ExecutionWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ExecutionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _pollIntervalMs = configuration.GetValue<int>("ExecutionSettings:QueuePollIntervalMs", 1000);
        _maxConcurrentExecutions = configuration.GetValue<int>("ExecutionSettings:MaxConcurrentExecutions", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Execution Worker started with max {MaxConcurrent} concurrent executions",
            _maxConcurrentExecutions);

        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(_maxConcurrentExecutions)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        var producerTask = ProduceSubmissionsAsync(channel.Writer, stoppingToken);

        var consumerTasks = Enumerable
            .Range(0, _maxConcurrentExecutions)
            .Select(_ => ConsumeSubmissionsAsync(channel.Reader, stoppingToken))
            .ToArray();

        await producerTask;
        channel.Writer.Complete();
        
        await Task.WhenAll(consumerTasks);

        _logger.LogInformation("Execution Worker stopped");
    }

    private async Task ProduceSubmissionsAsync(
        ChannelWriter<Guid> writer,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<ISubmissionQueue>();

                var submissionId = await queue.DequeueAsync(stoppingToken);

                if (submissionId.HasValue)
                {
                    await writer.WriteAsync(submissionId.Value, stoppingToken);
                    
                    _logger.LogDebug(
                        "Queued submission {SubmissionId} for processing",
                        submissionId.Value);
                }
                else
                {
                    await Task.Delay(_pollIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in producer loop");
                await Task.Delay(_pollIntervalMs, stoppingToken);
            }
        }
    }

    private async Task ConsumeSubmissionsAsync(
        ChannelReader<Guid> reader,
        CancellationToken stoppingToken)
    {
        await foreach (var submissionId in reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessSubmissionAsync(submissionId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing submission {SubmissionId}", submissionId);
            }
        }
    }

    private async Task ProcessSubmissionAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var executors = scope.ServiceProvider.GetRequiredService<IEnumerable<ICodeExecutor>>();

        _logger.LogInformation("Processing submission {SubmissionId}", submissionId);

        var submission = await dbContext.Submissions
            .Include(s => s.Language)
            .Include(s => s.Problem)
                .ThenInclude(p => p.TestCases.OrderBy(tc => tc.OrderIndex))
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (submission == null)
        {
            _logger.LogWarning("Submission {SubmissionId} not found", submissionId);
            return;
        }

        if (!submission.Problem.TestCases.Any())
        {
            _logger.LogWarning("Problem {ProblemId} has no test cases", submission.ProblemId);
            await UpdateSubmissionStatusAsync(dbContext, submission, SubmissionStatus.RuntimeError, cancellationToken);
            return;
        }

        submission.Status = SubmissionStatus.Running;
        await dbContext.SaveChangesAsync(cancellationToken);

        var executor = executors.Single();

        try
        {
            var result = await executor.ExecuteAsync(
                submission,
                submission.Language,
                submission.Problem.TestCases.ToList(),
                cancellationToken);

            await SaveExecutionResultsAsync(dbContext, submission, result, cancellationToken);

            _logger.LogInformation(
                "Submission {SubmissionId} completed with status {Status}, score {Score}%",
                submission.Id, result.Status, result.Score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution failed for submission {SubmissionId}", submissionId);
            await UpdateSubmissionStatusAsync(dbContext, submission, SubmissionStatus.RuntimeError, cancellationToken);
        }
    }

    private async Task SaveExecutionResultsAsync(
        ApplicationDbContext dbContext,
        Submission submission,
        Models.ExecutionResult result,
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

    private static async Task UpdateSubmissionStatusAsync(
        ApplicationDbContext dbContext,
        Submission submission,
        SubmissionStatus status,
        CancellationToken cancellationToken)
    {
        submission.Status = status;
        submission.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
