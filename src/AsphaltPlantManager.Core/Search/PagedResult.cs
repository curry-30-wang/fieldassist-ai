namespace AsphaltPlantManager.Core.Search;

public sealed class PagedResult : IReadOnlyList<SearchResult>
{
    private readonly IReadOnlyList<SearchResult> _items;

    public PagedResult(IReadOnlyList<SearchResult> items, int totalCount, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        _items = items.ToArray();
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<SearchResult> Items => _items;
    public int TotalCount { get; }
    public int Page { get; }
    public int CurrentPage => Page;
    public int PageSize { get; }
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public int Count => _items.Count;
    public SearchResult this[int index] => _items[index];
    public IEnumerator<SearchResult> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
