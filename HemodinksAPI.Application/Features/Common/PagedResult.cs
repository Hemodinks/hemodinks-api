namespace HemodinksAPI.Application.Features.Common;

public class PagedResult<T>
{
    public PagedResult()
    {
    }

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalItems)
    {
        Items = items.ToList();
        Page = page;
        PageSize = pageSize;
        TotalItems = totalItems;
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalItems / (double)pageSize) : 0;
    }

    public List<T> Items { get; set; } = [];

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }
}
