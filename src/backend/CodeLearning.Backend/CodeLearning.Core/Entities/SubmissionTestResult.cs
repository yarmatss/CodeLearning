using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class SubmissionTestResult : BaseEntity
{
    public Guid SubmissionId { get; set; }
    public Guid TestCaseId { get; set; }
    public TestResultStatus Status { get; set; }
    public string? ActualOutput { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsedKB { get; set; }

    public required Submission Submission { get; set; }
    public required TestCase TestCase { get; set; }
}
