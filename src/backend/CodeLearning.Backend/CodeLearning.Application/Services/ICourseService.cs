using CodeLearning.Application.DTOs.Common;
using CodeLearning.Application.DTOs.Course;

namespace CodeLearning.Application.Services;

public interface ICourseService
{
    Task<CourseResponseDto> CreateCourseAsync(CreateCourseDto dto, Guid instructorId);
    Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId, Guid? viewerId = null);
    Task<CourseStructureDto> GetCourseStructureAsync(Guid courseId, Guid? viewerId = null);
    Task<IEnumerable<CourseResponseDto>> GetInstructorCoursesAsync(Guid instructorId);
    Task<TeacherStatsDto> GetInstructorStatsAsync(Guid instructorId);
    Task<PagedResult<CourseResponseDto>> GetPublishedCoursesAsync(int? page = null, int? pageSize = null);
    Task<CourseResponseDto> UpdateCourseAsync(Guid courseId, UpdateCourseDto dto, Guid instructorId);

    // Idempotent: publishing a published course (or unpublishing a draft) returns it unchanged
    Task<CourseResponseDto> PublishCourseAsync(Guid courseId, Guid instructorId);
    Task<CourseResponseDto> UnpublishCourseAsync(Guid courseId, Guid instructorId);
    Task DeleteCourseAsync(Guid courseId, Guid instructorId);
}
