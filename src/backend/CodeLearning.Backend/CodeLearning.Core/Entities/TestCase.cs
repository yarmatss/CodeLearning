namespace CodeLearning.Core.Entities;

public class TestCase : BaseEntity
{
    public required string Input { get; set; }
    public required string ExpectedOutput { get; set; }
    public bool IsPublic { get; set; }
    public int OrderIndex { get; set; }
    public Guid ProblemId { get; set; }

    public required Problem Problem { get; set; }
    public ICollection<SubmissionTestResult> TestResults { get; init; } = [];
}
