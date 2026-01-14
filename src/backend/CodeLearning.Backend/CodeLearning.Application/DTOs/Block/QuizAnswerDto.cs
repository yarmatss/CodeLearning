namespace CodeLearning.Application.DTOs.Block;

public class QuizAnswerDto
{
    public required Guid Id { get; set; }
    public required string Text { get; set; }
    public required int OrderIndex { get; set; }
    // IsCorrect is only populated for instructors/admins editing blocks
    // Students should NOT see this field when viewing/attempting quizzes
    public bool? IsCorrect { get; set; }
}
