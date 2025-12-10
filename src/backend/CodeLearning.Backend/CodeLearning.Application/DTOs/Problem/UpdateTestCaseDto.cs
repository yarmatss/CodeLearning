namespace CodeLearning.Application.DTOs.Problem;

public class UpdateTestCaseDto
{
    public required string Input { get; set; }
    public required string ExpectedOutput { get; set; }
    public bool IsPublic { get; set; }
}
