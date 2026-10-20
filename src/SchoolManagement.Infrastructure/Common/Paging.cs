using Microsoft.EntityFrameworkCore;

namespace SchoolManagement.Infrastructure.Common;

public record PageQuery(int Page = 1, int PageSize = 25)
{
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > 200 ? 25 : PageSize;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.SafePage, page.SafePageSize, total);
    }
}
