using CodeLearning.Application.DTOs.Block;

namespace CodeLearning.Application.Services;

public interface IBlockService
{
    Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId);
    Task<Guid> CreateVideoBlockAsync(Guid subchapterId, CreateVideoBlockDto dto, Guid instructorId);
    Task<Guid> CreateQuizBlockAsync(Guid subchapterId, CreateQuizBlockDto dto, Guid instructorId);
    Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId);
    
    Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId, bool includeCorrectAnswers = false);
    Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId, bool includeCorrectAnswers = false);

    Task UpdateTheoryBlockAsync(Guid blockId, UpdateTheoryBlockDto dto, Guid instructorId);
    Task UpdateVideoBlockAsync(Guid blockId, UpdateVideoBlockDto dto, Guid instructorId);
    Task UpdateQuizBlockAsync(Guid blockId, UpdateQuizBlockDto dto, Guid instructorId);
    Task UpdateBlockOrderAsync(Guid blockId, int newOrderIndex, Guid instructorId);

    Task DeleteBlockAsync(Guid blockId, Guid instructorId);
}
