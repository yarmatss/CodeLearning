namespace CodeLearning.Application.DTOs.Course;

public class SubchapterStructureDto
{
    public Guid SubchapterId { get; set; }
    public required string Title { get; set; }
    public int OrderIndex { get; set; }
    public int BlocksCount { get; set; }
}
