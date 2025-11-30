namespace CodeLearning.Core.Entities;

public class QuizAnswerData
{
    public Guid QuestionId { get; set; }
    public List<Guid> SelectedAnswerIds { get; set; } = [];
    public bool IsCorrect { get; set; }
}
