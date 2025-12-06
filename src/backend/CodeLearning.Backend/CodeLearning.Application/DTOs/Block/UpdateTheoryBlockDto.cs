namespace CodeLearning.Application.DTOs.Block;

public class UpdateTheoryBlockDto
{
    public required string Title { get; set; }
    public required string Content { get; set; } // Markdown
}
