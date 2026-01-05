using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class QuizQuestion : BaseEntity
{
    public required string Content { get; set; }
    public QuestionType Type { get; set; }
    public int Points { get; set; } = 1;
    public int OrderIndex { get; set; }
    public string? Explanation { get; set; }
    public Guid QuizId { get; set; }

    public required Quiz Quiz { get; set; }
    public ICollection<QuizAnswer> Answers { get; init; } = [];
}
