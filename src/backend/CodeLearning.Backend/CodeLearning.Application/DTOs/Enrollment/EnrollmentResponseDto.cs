namespace CodeLearning.Application.DTOs.Enrollment;

public class EnrollmentResponseDto
{
    public required Guid CourseId { get; set; }
    public required string Message { get; set; }
    public required DateTimeOffset EnrolledAt { get; set; }
}
