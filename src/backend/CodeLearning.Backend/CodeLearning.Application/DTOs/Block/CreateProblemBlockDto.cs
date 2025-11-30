namespace CodeLearning.Application.DTOs.Block;

public class CreateProblemBlockDto
{
    public required string Title { get; set; }
    
    public required Guid ProblemId { get; set; }
}
