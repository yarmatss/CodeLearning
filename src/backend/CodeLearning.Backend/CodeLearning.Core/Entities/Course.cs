using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class Course : BaseEntity
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
    public Guid InstructorId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public required User Instructor { get; set; }
    public ICollection<Chapter> Chapters { get; init; } = [];
    public ICollection<StudentCourseProgress> StudentProgress { get; init; } = [];
    public ICollection<Certificate> Certificates { get; init; } = [];
}
