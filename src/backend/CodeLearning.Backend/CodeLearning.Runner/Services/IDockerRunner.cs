using CodeLearning.Core.Models;

namespace CodeLearning.Runner.Services;

public interface IDockerRunner
{
    Task<ExecutionResult> ExecuteAsync(
        Models.ExecutionContext context,
        CancellationToken cancellationToken = default);
}
