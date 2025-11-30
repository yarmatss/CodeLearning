using CodeLearning.Core.Enums;

namespace CodeLearning.Core.Entities;

public class CourseBlock : BaseEntity
{
    public required string Title { get; set; }
    public BlockType Type { get; set; }
    public int OrderIndex { get; set; }
    public Guid SubchapterId { get; set; }

    public Guid? TheoryContentId { get; set; }
    public Guid? VideoContentId { get; set; }
    public Guid? QuizId { get; set; }
    public Guid? ProblemId { get; set; }

    public required Subchapter Subchapter { get; set; }
    public TheoryContent? TheoryContent { get; set; }
    public VideoContent? VideoContent { get; set; }
    public Quiz? Quiz { get; set; }
    public Problem? Problem { get; set; }

    public ICollection<StudentBlockProgress> StudentProgress { get; init; } = [];
    public ICollection<Comment> Comments { get; init; } = [];
}
