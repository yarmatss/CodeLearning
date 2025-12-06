namespace CodeLearning.Application.DTOs.Progress;

public class CompleteBlockResponseDto
{
    public required Guid BlockId { get; set; }
    public required DateTimeOffset CompletedAt { get; set; }
    public Guid? NextBlockId { get; set; }
    public required string Message { get; set; }
    public bool CourseCompleted { get; set; }
}
