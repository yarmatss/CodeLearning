namespace CodeLearning.Application.DTOs.Block;

public class QuizQuestionDto
{
    public required Guid Id { get; set; }
    public required string Content { get; set; }
    public required string Type { get; set; } // "SingleChoice" | "MultipleChoice" | "TrueFalse"
    public required int Points { get; set; }
    public string? Explanation { get; set; }
    public required int OrderIndex { get; set; }
    public required List<QuizAnswerDto> Answers { get; set; }
}
