namespace CodeLearning.Application.DTOs.Problem;

public class ProblemResponseDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Difficulty { get; set; }
    public Guid AuthorId { get; set; }
    public required string AuthorName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // True when a published course uses the problem; it can't be edited until the course is unpublished
    public bool IsUsedByPublishedCourse { get; set; }
    public List<TestCaseResponseDto> TestCases { get; set; } = [];
    public List<StarterCodeResponseDto> StarterCodes { get; set; } = [];
    public List<TagResponseDto> Tags { get; set; } = [];
}