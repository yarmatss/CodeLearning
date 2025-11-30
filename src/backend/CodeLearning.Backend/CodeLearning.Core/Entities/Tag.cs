namespace CodeLearning.Core.Entities;

public class Tag : BaseEntity
{
    public required string Name { get; set; }

    public ICollection<ProblemTag> ProblemTags { get; init; } = [];
}
