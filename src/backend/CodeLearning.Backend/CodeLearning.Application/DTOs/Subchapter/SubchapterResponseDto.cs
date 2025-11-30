namespace CodeLearning.Application.DTOs.Subchapter;

public class SubchapterResponseDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required int OrderIndex { get; set; }
    public required Guid ChapterId { get; set; }
    public int BlocksCount { get; set; }
}
