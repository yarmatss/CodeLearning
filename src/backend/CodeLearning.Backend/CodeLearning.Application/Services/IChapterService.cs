using CodeLearning.Application.DTOs.Chapter;

namespace CodeLearning.Application.Services;

public interface IChapterService
{
    Task<ChapterResponseDto> AddChapterAsync(Guid courseId, CreateChapterDto dto, Guid instructorId);
    Task<IEnumerable<ChapterResponseDto>> GetCourseChaptersAsync(Guid courseId);
    Task<ChapterResponseDto> UpdateChapterOrderAsync(Guid chapterId, int newOrderIndex, Guid instructorId);
    Task DeleteChapterAsync(Guid chapterId, Guid instructorId);
}
