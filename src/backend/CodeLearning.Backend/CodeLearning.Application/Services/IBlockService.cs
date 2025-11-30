using CodeLearning.Application.DTOs.Block;

namespace CodeLearning.Application.Services;

public interface IBlockService
{
    Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId);
    Task<Guid> CreateVideoBlockAsync(Guid subchapterId, CreateVideoBlockDto dto, Guid instructorId);
    Task<Guid> CreateQuizBlockAsync(Guid subchapterId, CreateQuizBlockDto dto, Guid instructorId);
    Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId);

    Task DeleteBlockAsync(Guid blockId, Guid instructorId);
    Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId);
    Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId);
}
