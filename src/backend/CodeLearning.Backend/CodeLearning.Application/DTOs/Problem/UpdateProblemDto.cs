namespace CodeLearning.Application.DTOs.Problem;

public class UpdateProblemDto
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Difficulty { get; set; }
    public List<Guid> TagIds { get; set; } = [];
}
