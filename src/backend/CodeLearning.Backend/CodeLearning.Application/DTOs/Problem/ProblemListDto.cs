namespace CodeLearning.Application.DTOs.Problem;

public class ProblemListDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Difficulty { get; set; }
    public required string AuthorName { get; set; }
    public int TestCasesCount { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
