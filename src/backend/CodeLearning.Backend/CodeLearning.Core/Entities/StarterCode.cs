namespace CodeLearning.Core.Entities;

public class StarterCode : BaseEntity
{
    public required string Code { get; set; }
    public Guid LanguageId { get; set; }
    public Guid ProblemId { get; set; }

    public required Language Language { get; set; }
    public required Problem Problem { get; set; }
}
