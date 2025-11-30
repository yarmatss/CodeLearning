namespace CodeLearning.Core.Entities;

public class ProblemTag
{
    public Guid ProblemId { get; set; }
    public Guid TagId { get; set; }

    public required Problem Problem { get; set; }
    public required Tag Tag { get; set; }
}
