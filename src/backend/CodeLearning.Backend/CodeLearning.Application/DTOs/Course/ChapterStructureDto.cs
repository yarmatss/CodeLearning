namespace CodeLearning.Application.DTOs.Course;

public class ChapterStructureDto
{
    public Guid ChapterId { get; set; }
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public List<SubchapterStructureDto> Subchapters { get; set; } = [];
}
