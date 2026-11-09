using CodeLearning.Application.DTOs.Common;

namespace CodeLearning.Application.DTOs.Problem;

public class ProblemListQuery
{
    public const int DefaultPageSize = PagedResult<ProblemListDto>.DefaultPageSize;
    public const int MaxPageSize = PagedResult<ProblemListDto>.MaxPageSize;

    public string? Difficulty { get; set; }
    public Guid? TagId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
}
