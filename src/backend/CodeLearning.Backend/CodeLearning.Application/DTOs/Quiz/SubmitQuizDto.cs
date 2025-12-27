namespace CodeLearning.Application.DTOs.Quiz;

public class SubmitQuizDto
{
    public required List<QuizAnswerSubmissionDto> Answers { get; set; }
}
