namespace CodeLearning.Application.DTOs.Course;

public class CourseStructureDto
{
    public Guid CourseId { get; set; }
    public required string CourseTitle { get; set; }
    public required string CourseDescription { get; set; }
    public int TotalBlocksCount { get; set; }
    public List<ChapterStructureDto> Chapters { get; set; } = [];
}