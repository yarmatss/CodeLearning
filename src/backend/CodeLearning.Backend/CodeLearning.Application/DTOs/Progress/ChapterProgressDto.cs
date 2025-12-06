namespace CodeLearning.Application.DTOs.Progress;

public class ChapterProgressDto
{
    public required Guid ChapterId { get; set; }
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public required List<SubchapterProgressDto> Subchapters { get; set; }
}
