namespace UPBazaar.SharedKernel.Results;

/// <summary>One page of results plus the counters a client needs to walk the rest.</summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}
