namespace SchoolManagement.Web.Models;

/// <summary>Mirrors SchoolManagement.Infrastructure.Common.PagedResult. List endpoints take ?page=&amp;pageSize= (max 200).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
