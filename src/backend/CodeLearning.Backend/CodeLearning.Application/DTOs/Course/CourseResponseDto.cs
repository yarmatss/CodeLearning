using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Course;

public class CourseResponseDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required CourseStatus Status { get; set; }
    public required Guid InstructorId { get; set; }
    public required string InstructorName { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int ChaptersCount { get; set; }
    public int TotalBlocks { get; set; }
}
