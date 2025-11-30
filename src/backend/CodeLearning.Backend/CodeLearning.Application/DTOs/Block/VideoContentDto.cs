namespace CodeLearning.Application.DTOs.Block;

public class VideoContentDto
{
    public required Guid Id { get; set; }
    public required string VideoUrl { get; set; }
    public required string VideoId { get; set; } // For embedding
    public int? DurationSeconds { get; set; }
}
