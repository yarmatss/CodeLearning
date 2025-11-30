namespace CodeLearning.Application.DTOs.Block;

public class QuizAnswerDto
{
    public required Guid Id { get; set; }
    public required string Text { get; set; }
    public required int OrderIndex { get; set; }
    // IsCorrect is not exposed to students
}
