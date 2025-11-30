namespace CodeLearning.Core.Entities;

public class QuizAnswer : BaseEntity
{
    public required string Text { get; set; }
    public bool IsCorrect { get; set; }
    public int OrderIndex { get; set; }
    public Guid QuestionId { get; set; }

    public required QuizQuestion Question { get; set; }
}
