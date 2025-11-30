namespace CodeLearning.Core.Entities;

public class Quiz : BaseEntity
{
    public required CourseBlock Block { get; set; }
    public ICollection<QuizQuestion> Questions { get; init; } = [];
    public ICollection<StudentQuizAttempt> StudentAttempts { get; init; } = [];
}
