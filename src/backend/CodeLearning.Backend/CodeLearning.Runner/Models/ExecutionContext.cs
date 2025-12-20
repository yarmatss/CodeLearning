using CodeLearning.Core.Entities;

namespace CodeLearning.Runner.Models;

public class ExecutionContext
{
    public required Submission Submission { get; set; }
    public required Language Language { get; set; }
    public required List<TestCase> TestCases { get; set; }
    public required string WorkspaceDirectory { get; set; }
}
