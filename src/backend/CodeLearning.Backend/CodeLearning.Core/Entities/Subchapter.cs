namespace CodeLearning.Core.Entities;

public class Subchapter : BaseEntity
{
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public Guid ChapterId { get; set; }

    public required Chapter Chapter { get; set; }
    public ICollection<CourseBlock> Blocks { get; init; } = [];
}
