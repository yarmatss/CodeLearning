using CodeLearning.Application.DTOs.Progress;

namespace CodeLearning.Application.Services;

public interface IProgressService
{
    Task<CompleteBlockResponseDto> CompleteBlockAsync(Guid blockId, Guid studentId);
    Task<CourseProgressDto> GetCourseProgressAsync(Guid courseId, Guid studentId);
    Task<Guid?> GetNextBlockAsync(Guid courseId, Guid studentId);
}
