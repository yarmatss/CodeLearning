using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Enrollment;

public class EnrolledCourseDto
{
    public required Guid CourseId { get; set; }
    public required string CourseTitle { get; set; }
    public required string CourseDescription { get; set; }

    // Enrollments survive unpublishing; the content is unavailable until the course is published again
    public required CourseStatus CourseStatus { get; set; }
    public required string InstructorName { get; set; }
    public required DateTimeOffset EnrolledAt { get; set; }
    public required DateTimeOffset LastActivityAt { get; set; }
    public Guid? CurrentBlockId { get; set; }
    public int CompletedBlocksCount { get; set; }
    public int TotalBlocksCount { get; set; }
    public double ProgressPercentage { get; set; }
}
