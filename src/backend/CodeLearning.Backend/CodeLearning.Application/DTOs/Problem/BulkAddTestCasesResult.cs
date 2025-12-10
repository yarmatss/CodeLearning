namespace CodeLearning.Application.DTOs.Problem;

public class BulkAddTestCasesResult
{
    public int Added { get; set; }
    public List<TestCaseResponseDto> TestCases { get; set; } = [];
}
