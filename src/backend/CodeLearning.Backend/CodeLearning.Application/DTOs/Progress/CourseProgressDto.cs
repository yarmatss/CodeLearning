namespace CodeLearning.Application.DTOs.Progress;

public class CourseProgressDto
{
    public required Guid CourseId { get; set; }
    public required string CourseTitle { get; set; }
    public required DateTimeOffset EnrolledAt { get; set; }
    public required DateTimeOffset LastActivityAt { get; set; }
    public Guid? CurrentBlockId { get; set; }
    public int CompletedBlocksCount { get; set; }
    public int TotalBlocksCount { get; set; }
    public double ProgressPercentage { get; set; }
    public required List<ChapterProgressDto> Chapters { get; set; }
}