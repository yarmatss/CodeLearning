namespace CodeLearning.Application.DTOs.Block;

public class UpdateVideoBlockDto
{
    public required string Title { get; set; }
    public required string VideoUrl { get; set; } // YouTube URL
}
