namespace CodeLearning.Application.DTOs.Progress;

public class BlockProgressDto
{
    public required Guid BlockId { get; set; }
    public required string Title { get; set; }
    public required string Type { get; set; }
    public int OrderIndex { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
