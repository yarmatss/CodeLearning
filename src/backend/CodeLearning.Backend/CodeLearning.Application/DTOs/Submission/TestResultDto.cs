using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Submission;

public class TestResultDto
{
    public Guid TestCaseId { get; set; }
    public TestResultStatus Status { get; set; }
    public string? Input { get; set; }
    public string? ExpectedOutput { get; set; }
    public string? ActualOutput { get; set; }
    public string? ErrorMessage { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsedKB { get; set; }
    public bool IsPublic { get; set; }
}
