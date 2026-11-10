using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Course;

// Outline entry only: the public structure never carries block content
public class BlockStructureDto
{
    public Guid BlockId { get; set; }
    public required string Title { get; set; }
    public BlockType Type { get; set; }
    public int OrderIndex { get; set; }
}
