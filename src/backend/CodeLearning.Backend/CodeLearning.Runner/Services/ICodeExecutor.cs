using CodeLearning.Core.Entities;
using CodeLearning.Core.Models;

namespace CodeLearning.Runner.Services;

public interface ICodeExecutor
{  
    Task<ExecutionResult> ExecuteAsync(
        Submission submission,
        Language language,
        List<TestCase> testCases,
        CancellationToken cancellationToken = default);
}
