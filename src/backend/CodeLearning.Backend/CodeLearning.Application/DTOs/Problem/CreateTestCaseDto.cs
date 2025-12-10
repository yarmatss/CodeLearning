namespace CodeLearning.Application.DTOs.Problem;

public class CreateTestCaseDto
{
    public required string Input { get; set; }
    public required string ExpectedOutput { get; set; }
    public bool IsPublic { get; set; }
}
