using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class Submission : BaseEntity
{
    public Guid ProblemId { get; set; }
    public Guid StudentId { get; set; }
    public Guid LanguageId { get; set; }
    public required string Code { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public int Score { get; set; }
    public int? ExecutionTimeMs { get; set; }
    public int? MemoryUsedKB { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public required Problem Problem { get; set; }
    public required User Student { get; set; }
    public required Language Language { get; set; }
    public ICollection<SubmissionTestResult> TestResults { get; init; } = [];
}
