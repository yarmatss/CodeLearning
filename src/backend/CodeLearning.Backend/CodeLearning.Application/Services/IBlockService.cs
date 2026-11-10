using CodeLearning.Application.DTOs.Block;

namespace CodeLearning.Application.Services;

// Mutations take the subchapter id from the route: a block that lives elsewhere is answered with 404
public interface IBlockService
{
    Task<Guid> CreateTheoryBlockAsync(Guid subchapterId, CreateTheoryBlockDto dto, Guid instructorId);
    Task<Guid> CreateVideoBlockAsync(Guid subchapterId, CreateVideoBlockDto dto, Guid instructorId);
    Task<Guid> CreateQuizBlockAsync(Guid subchapterId, CreateQuizBlockDto dto, Guid instructorId);
    Task<Guid> CreateProblemBlockAsync(Guid subchapterId, CreateProblemBlockDto dto, Guid instructorId);

    // Content is readable by the course instructor, or by enrolled students while the course is published
    Task<BlockResponseDto> GetBlockByIdAsync(Guid blockId, Guid viewerId);
    Task<IEnumerable<BlockResponseDto>> GetSubchapterBlocksAsync(Guid subchapterId, Guid viewerId);

    Task UpdateTheoryBlockAsync(Guid subchapterId, Guid blockId, UpdateTheoryBlockDto dto, Guid instructorId);
    Task UpdateVideoBlockAsync(Guid subchapterId, Guid blockId, UpdateVideoBlockDto dto, Guid instructorId);
    Task UpdateQuizBlockAsync(Guid subchapterId, Guid blockId, UpdateQuizBlockDto dto, Guid instructorId);
    Task UpdateProblemBlockAsync(Guid subchapterId, Guid blockId, UpdateProblemBlockDto dto, Guid instructorId);
    Task UpdateBlockOrderAsync(Guid subchapterId, Guid blockId, int newOrderIndex, Guid instructorId);

    Task DeleteBlockAsync(Guid subchapterId, Guid blockId, Guid instructorId);
}
