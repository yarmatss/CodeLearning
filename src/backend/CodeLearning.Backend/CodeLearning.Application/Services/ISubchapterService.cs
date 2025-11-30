using CodeLearning.Application.DTOs.Subchapter;

namespace CodeLearning.Application.Services;

public interface ISubchapterService
{
    Task<SubchapterResponseDto> AddSubchapterAsync(Guid chapterId, CreateSubchapterDto dto, Guid instructorId);
    Task<IEnumerable<SubchapterResponseDto>> GetChapterSubchaptersAsync(Guid chapterId);
    Task<SubchapterResponseDto> UpdateSubchapterOrderAsync(Guid subchapterId, int newOrderIndex, Guid instructorId);
    Task DeleteSubchapterAsync(Guid subchapterId, Guid instructorId);
}
