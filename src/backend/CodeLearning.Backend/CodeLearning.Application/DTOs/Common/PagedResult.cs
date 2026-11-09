namespace CodeLearning.Application.DTOs.Common;

public class PagedResult<T>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public required List<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    // Out-of-range paging parameters are clamped rather than rejected, so a stale link still returns a page
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
