namespace CodeLearning.Core.Entities;

public class StudentCourseProgress : BaseEntity
{
    public Guid StudentId { get; set; }
    public Guid CourseId { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public Guid? CurrentBlockId { get; set; }

    public required User Student { get; set; }
    public required Course Course { get; set; }
}
