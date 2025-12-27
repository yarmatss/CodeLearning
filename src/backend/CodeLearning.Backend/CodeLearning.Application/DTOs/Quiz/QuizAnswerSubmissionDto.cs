namespace CodeLearning.Application.DTOs.Quiz;

public class QuizAnswerSubmissionDto
{
    public required Guid QuestionId { get; set; }
    public required List<Guid> SelectedAnswerIds { get; set; }
}
