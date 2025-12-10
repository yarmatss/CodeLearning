namespace CodeLearning.Application.DTOs.Problem;

public class TestCaseResponseDto
{
    public Guid Id { get; set; }
    public required string Input { get; set; }
    public required string ExpectedOutput { get; set; }
    public bool IsPublic { get; set; }
    public int OrderIndex { get; set; }
}
