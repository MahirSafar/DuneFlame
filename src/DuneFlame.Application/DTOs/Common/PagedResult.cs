namespace DuneFlame.Application.DTOs.Common;

public class PagedResult<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }

    public PagedResult()
    {
        Items = new List<T>();
        HasPreviousPage = false;
        HasNextPage = false;
    }

    public PagedResult(List<T> items, int totalCount, int pageNumber, int pageSize, int totalPages)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalPages = totalPages;
        HasPreviousPage = pageNumber > 1;
        HasNextPage = pageNumber < totalPages;
    }
}
