using CodeLearning.Application.DTOs.Enrollment;

namespace CodeLearning.Application.Services;

public interface IEnrollmentService
{
    Task<EnrollmentResponseDto> EnrollAsync(Guid courseId, Guid studentId);
    Task UnenrollAsync(Guid courseId, Guid studentId);
    Task<IEnumerable<EnrolledCourseDto>> GetEnrolledCoursesAsync(Guid studentId);
    Task<bool> IsEnrolledAsync(Guid courseId, Guid studentId);
}
