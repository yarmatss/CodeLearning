using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Enrollment;

namespace CodeLearning.Application.Services;

public interface IEnrollmentService
{
    Task<EnrollmentResponseDto> EnrollAsync(Guid courseId, Guid studentId);
    Task UnenrollAsync(Guid courseId, Guid studentId);
    Task<PagedResult<EnrolledCourseDto>> GetEnrolledCoursesAsync(Guid studentId, int? page = null, int? pageSize = null);
    Task<bool> IsEnrolledAsync(Guid courseId, Guid studentId);
}
