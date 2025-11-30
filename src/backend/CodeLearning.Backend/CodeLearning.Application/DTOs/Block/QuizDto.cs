namespace CodeLearning.Application.DTOs.Block;

public class QuizDto
{
    public required Guid Id { get; set; }
    public required List<QuizQuestionDto> Questions { get; set; }
}
