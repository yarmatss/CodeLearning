namespace CodeLearning.Application.DTOs.Problem;

public class ProblemListDto
{
    // Longer descriptions are cut to this many characters and end with "..."
    public const int DescriptionPreviewLength = 200;

    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Difficulty { get; set; }
    public Guid AuthorId { get; set; }
    public required string AuthorName { get; set; }
    public int TestCasesCount { get; set; }
    public int LanguagesCount { get; set; }
    public bool IsUsedByPublishedCourse { get; set; }
    public List<TagResponseDto> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
