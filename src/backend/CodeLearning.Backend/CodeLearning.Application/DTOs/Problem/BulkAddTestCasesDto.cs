namespace CodeLearning.Application.DTOs.Problem;

public class BulkAddTestCasesDto
{
    public required List<CreateTestCaseDto> TestCases { get; set; }
}
