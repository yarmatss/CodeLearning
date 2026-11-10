using CodeLearning.Application.DTOs.Chapter;

namespace CodeLearning.Application.Services;

// Mutations take the course id from the route: a chapter of another course is answered with 404
public interface IChapterService
{
    Task<ChapterResponseDto> AddChapterAsync(Guid courseId, CreateChapterDto dto, Guid instructorId);
    Task<IEnumerable<ChapterResponseDto>> GetCourseChaptersAsync(Guid courseId, Guid? viewerId = null);
    Task<ChapterResponseDto> RenameChapterAsync(Guid courseId, Guid chapterId, UpdateChapterDto dto, Guid instructorId);
    Task<ChapterResponseDto> UpdateChapterOrderAsync(Guid courseId, Guid chapterId, int newOrderIndex, Guid instructorId);
    Task DeleteChapterAsync(Guid courseId, Guid chapterId, Guid instructorId);
}
