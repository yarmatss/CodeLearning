using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Models;

public class ExecutionResult
{
    public SubmissionStatus Status { get; set; }
    public int Score { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int? TotalExecutionTimeMs { get; set; }
    public int? MaxMemoryUsedKB { get; set; }
    public List<TestCaseResult> TestResults { get; set; } = [];
    public string? CompilationError { get; set; }
    public string? RuntimeError { get; set; }
}
