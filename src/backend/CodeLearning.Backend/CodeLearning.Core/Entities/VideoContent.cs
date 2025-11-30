namespace CodeLearning.Core.Entities;

public class VideoContent : BaseEntity
{
    public required string VideoUrl { get; set; }
    public required string VideoId { get; set; }
    public int? DurationSeconds { get; set; }

    public required CourseBlock Block { get; set; }
}
