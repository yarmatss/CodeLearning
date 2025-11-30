using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class TheoryContentExtensions
{
    public static TheoryContentDto? ToDto(this TheoryContent? content)
    {
        if (content is null) return null;

        return new TheoryContentDto
        {
            Id = content.Id,
            Content = content.Content
        };
    }
}
