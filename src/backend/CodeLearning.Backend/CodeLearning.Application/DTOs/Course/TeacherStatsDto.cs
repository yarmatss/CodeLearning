namespace CodeLearning.Application.DTOs.Course;

public class TeacherStatsDto
{
    public required int PublishedCourses { get; init; }
    public required int DraftCourses { get; init; }

    // Distinct students enrolled in any of the teacher's courses
    public required int TotalStudents { get; init; }
}
