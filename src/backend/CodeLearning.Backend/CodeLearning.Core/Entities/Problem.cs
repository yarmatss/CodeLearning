using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class Problem : BaseEntity
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DifficultyLevel Difficulty { get; set; }
    public Guid AuthorId { get; set; }

    public required User Author { get; set; }
    public ICollection<TestCase> TestCases { get; init; } = [];
    public ICollection<StarterCode> StarterCodes { get; init; } = [];
    public ICollection<ProblemTag> ProblemTags { get; init; } = [];
    public ICollection<CourseBlock> Blocks { get; init; } = [];
    public ICollection<Submission> Submissions { get; init; } = [];
}
