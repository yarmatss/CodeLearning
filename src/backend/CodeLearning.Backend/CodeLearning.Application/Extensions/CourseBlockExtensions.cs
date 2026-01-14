using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class CourseBlockExtensions
{
    public static BlockResponseDto ToResponseDto(this CourseBlock block, bool includeCorrectAnswers = false)
    {
        ArgumentNullException.ThrowIfNull(block);

        var dto = new BlockResponseDto
        {
            Id = block.Id,
            Title = block.Title,
            Type = block.Type,
            OrderIndex = block.OrderIndex,
            SubchapterId = block.SubchapterId
        };

        dto.TheoryContent = block.TheoryContent?.ToDto();
        dto.VideoContent = block.VideoContent?.ToDto();
        dto.Quiz = block.Quiz?.ToDto(includeCorrectAnswers);
        dto.Problem = block.Problem?.ToDto();

        return dto;
    }

    public static IEnumerable<BlockResponseDto> ToResponseDtos(this IEnumerable<CourseBlock> blocks, bool includeCorrectAnswers = false)
    {
        return blocks.Select(b => b.ToResponseDto(includeCorrectAnswers));
    }
}
