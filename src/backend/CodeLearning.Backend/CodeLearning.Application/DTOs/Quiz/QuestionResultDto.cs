namespace CodeLearning.Application.DTOs.Quiz;

public class QuestionResultDto
{
    public required Guid QuestionId { get; set; }
    public required string QuestionContent { get; set; }
    public required string QuestionType { get; set; }
    public required bool IsCorrect { get; set; }
    public required int Points { get; set; }
    public required List<Guid> SelectedAnswerIds { get; set; }
    public required List<AnswerFeedbackDto> Answers { get; set; }
    public string? Explanation { get; set; }
}
