using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Submission;

public class SubmissionResponseDto
{
    public Guid Id { get; set; }
    public Guid ProblemId { get; set; }
    public string ProblemTitle { get; set; } = string.Empty;
    public Guid LanguageId { get; set; }
    public string LanguageName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public SubmissionStatus Status { get; set; }
    public int Score { get; set; }
    public int TotalTestCases { get; set; }
    public int PassedTestCases { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsedKB { get; set; }
    public string? CompilationError { get; set; }
    public string? RuntimeError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<TestResultDto>? TestResults { get; set; }
}