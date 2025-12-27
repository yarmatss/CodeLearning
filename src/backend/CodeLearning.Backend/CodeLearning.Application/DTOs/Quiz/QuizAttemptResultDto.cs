namespace CodeLearning.Application.DTOs.Quiz;

public class QuizAttemptResultDto
{
    public required Guid AttemptId { get; set; }
    public required Guid QuizId { get; set; }
    public required int Score { get; set; }
    public required int MaxScore { get; set; }
    public required double Percentage { get; set; }
    public required DateTimeOffset AttemptedAt { get; set; }
    public required List<QuestionResultDto> QuestionResults { get; set; }
}