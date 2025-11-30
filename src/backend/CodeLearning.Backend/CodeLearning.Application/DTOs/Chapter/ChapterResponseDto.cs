namespace CodeLearning.Application.DTOs.Chapter;

public class ChapterResponseDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required int OrderIndex { get; set; }
    public required Guid CourseId { get; set; }
    public int SubchaptersCount { get; set; }
}
