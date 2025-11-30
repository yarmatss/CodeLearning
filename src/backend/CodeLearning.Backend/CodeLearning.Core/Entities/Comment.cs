namespace CodeLearning.Core.Entities;

public class Comment : BaseEntity
{
    public Guid BlockId { get; set; }
    public Guid UserId { get; set; }
    public required string Content { get; set; }
    public Guid? ParentCommentId { get; set; }

    public required CourseBlock Block { get; set; }
    public required User User { get; set; }
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; init; } = [];
}
