namespace CodeLearning.Core.Entities;

public class StudentQuizAttempt : BaseEntity
{
    public Guid StudentId { get; set; }
    public Guid QuizId { get; set; }
    public int Score { get; set; }
    public List<QuizAnswerData> Answers { get; set; } = [];
    public DateTimeOffset AttemptedAt { get; set; }

    public required User Student { get; set; }
    public required Quiz Quiz { get; set; }
}
