using CodeLearning.Application.DTOs.Block;
using CodeLearning.Core.Entities;

namespace CodeLearning.Application.Extensions;

public static class ProblemExtensions
{
    public static ProblemDto? ToDto(this Problem? problem)
    {
        if (problem is null) return null;

        return new ProblemDto
        {
            Id = problem.Id,
            Title = problem.Title,
            Description = problem.Description,
            Difficulty = problem.Difficulty.ToString()
        };
    }
}
