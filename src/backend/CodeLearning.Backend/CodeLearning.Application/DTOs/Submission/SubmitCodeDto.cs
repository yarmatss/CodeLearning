namespace CodeLearning.Application.DTOs.Submission;

public class SubmitCodeDto
{
    public Guid ProblemId { get; set; }
    public Guid LanguageId { get; set; }
    public required string Code { get; set; }
}
