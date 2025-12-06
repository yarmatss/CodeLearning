namespace CodeLearning.Application.DTOs.Progress;

public class SubchapterProgressDto
{
    public required Guid SubchapterId { get; set; }
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public required List<BlockProgressDto> Blocks { get; set; }
}
