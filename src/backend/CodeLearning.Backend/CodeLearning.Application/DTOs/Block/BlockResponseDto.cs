using CodeLearning.Core.Enums;

namespace CodeLearning.Application.DTOs.Block;

public class BlockResponseDto
{
    public required Guid Id { get; set; }
    public required string Title { get; set; }
    public required BlockType Type { get; set; }
    public required int OrderIndex { get; set; }
    public required Guid SubchapterId { get; set; }
    
    public TheoryContentDto? TheoryContent { get; set; }
    public VideoContentDto? VideoContent { get; set; }
    public QuizDto? Quiz { get; set; }
    public ProblemDto? Problem { get; set; }
}
