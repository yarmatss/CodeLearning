namespace CodeLearning.Application.DTOs.Block;

public class CreateQuizAnswerDto
{
    public required string AnswerText { get; set; }
    
    public bool IsCorrect { get; set; }
}
