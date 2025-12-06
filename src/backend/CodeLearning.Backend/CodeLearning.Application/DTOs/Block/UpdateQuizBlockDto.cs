namespace CodeLearning.Application.DTOs.Block;

public class UpdateQuizBlockDto
{
    public required string Title { get; set; }
    public required List<CreateQuizQuestionDto> Questions { get; set; }
}
