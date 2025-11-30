namespace CodeLearning.Core.Entities;

public class StudentBlockProgress : BaseEntity
{
    public Guid StudentId { get; set; }
    public Guid BlockId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public required User Student { get; set; }
    public required CourseBlock Block { get; set; }
}
