using CodeLearning.Core.Entities;
using CodeLearning.Runner.Models;

namespace CodeLearning.Runner.Services;

public interface ICodeExecutor
{
    string LanguageName { get; }
    
    Task<ExecutionResult> ExecuteAsync(
        Submission submission,
        Language language,
        List<TestCase> testCases,
        CancellationToken cancellationToken = default);
}
