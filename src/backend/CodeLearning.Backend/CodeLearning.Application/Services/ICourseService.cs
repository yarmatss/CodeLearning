using CodeLearning.Application.DTOs.Course;

namespace CodeLearning.Application.Services;

public interface ICourseService
{
    Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId);
    Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId);
    Task<CourseStructureDto> GetCourseStructureAsync(Guid courseId);
    Task<IEnumerable<CourseResponseDto>> GetInstructorCoursesAsync(Guid instructorId);
    Task<IEnumerable<CourseResponseDto>> GetPublishedCoursesAsync();
    Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId);
    Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId);
    Task DeleteCourseAsync(Guid courseId, Guid instructorId);
}
