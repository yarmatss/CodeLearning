using CodeLearning.Application.DTOs.Subchapter;

namespace CodeLearning.Application.Services;

// Mutations take the chapter id from the route: a subchapter of another chapter is answered with 404
public interface ISubchapterService
{
    Task<SubchapterResponseDto> AddSubchapterAsync(Guid chapterId, CreateSubchapterDto dto, Guid instructorId);
    Task<IEnumerable<SubchapterResponseDto>> GetChapterSubchaptersAsync(Guid chapterId, Guid? viewerId = null);
    Task<SubchapterResponseDto> RenameSubchapterAsync(Guid chapterId, Guid subchapterId, UpdateSubchapterDto dto, Guid instructorId);
    Task<SubchapterResponseDto> UpdateSubchapterOrderAsync(Guid chapterId, Guid subchapterId, int newOrderIndex, Guid instructorId);
    Task DeleteSubchapterAsync(Guid chapterId, Guid subchapterId, Guid instructorId);
}
