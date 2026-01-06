using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class VideoContentExtensions
{
    public static VideoContentDto? ToDto(this VideoContent? content)
    {
        if (content is null) return null;

        return new VideoContentDto
        {
            Id = content.Id,
            VideoUrl = content.VideoUrl,
            VideoId = content.VideoId,
        };
    }
}
