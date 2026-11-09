namespace CodeLearning.Application.DTOs.Block;

public class UpdateProblemBlockDto
{
    public required string Title { get; set; }
    public required Guid ProblemId { get; set; }
}
