namespace CodeLearning.Application.DTOs.Problem;

public class CreateProblemDto
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Difficulty { get; set; }
    public List<CreateTestCaseDto> TestCases { get; set; } = [];
    public List<CreateStarterCodeDto> StarterCodes { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
}