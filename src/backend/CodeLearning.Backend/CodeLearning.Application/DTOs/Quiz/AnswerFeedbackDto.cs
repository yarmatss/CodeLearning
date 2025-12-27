namespace CodeLearning.Application.DTOs.Quiz;

public class AnswerFeedbackDto
{
    public required Guid AnswerId { get; set; }
    public required string Text { get; set; }
    public required bool IsCorrect { get; set; }
    public required bool WasSelected { get; set; }
}
