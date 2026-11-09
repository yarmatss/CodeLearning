namespace CodeLearning.Application.DTOs.Quiz;

public class QuizAttemptResultDto
{
    public required Guid AttemptId { get; set; }
    public required Guid QuizId { get; set; }

    // Points earned, out of MaxScore
    public required int Score { get; set; }
    public required int MaxScore { get; set; }

    // Score as a percentage of MaxScore (0-100, two decimals)
    public required double Percentage { get; set; }
    public required DateTimeOffset AttemptedAt { get; set; }
    public required List<QuestionResultDto> QuestionResults { get; set; }
}
