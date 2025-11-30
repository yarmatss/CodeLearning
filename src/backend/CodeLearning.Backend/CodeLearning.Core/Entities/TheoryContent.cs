namespace CodeLearning.Core.Entities;

public class TheoryContent : BaseEntity
{
    public required string Content { get; set; }

    public required CourseBlock Block { get; set; }
}
