namespace CodeLearning.Core.Entities;

public class Chapter : BaseEntity
{
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public Guid CourseId { get; set; }

    public required Course Course { get; set; }
    public ICollection<Subchapter> Subchapters { get; init; } = [];
}
