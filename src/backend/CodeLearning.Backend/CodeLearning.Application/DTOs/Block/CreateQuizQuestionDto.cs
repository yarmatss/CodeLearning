namespace CodeLearning.Application.DTOs.Block;

public class CreateQuizQuestionDto
{
    public required string QuestionText { get; set; }
    public required string Type { get; set; } // "SingleChoice" | "MultipleChoice" | "TrueFalse"
    public int Points { get; set; } = 1;
    public required List<CreateQuizAnswerDto> Answers { get; set; }
}
