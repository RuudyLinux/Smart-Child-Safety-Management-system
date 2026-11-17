namespace ChildGuard.Application.Common;

/// <summary>
/// Optional server-side paging for list endpoints. Absent (<c>page</c> not given) means "the whole list, exactly
/// as before", so existing API clients never see a change. Sort names are whitelisted per list.
/// </summary>
public sealed record PageRequest(int Page, int PageSize, string? Sort, bool Descending)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public int Skip => (Page - 1) * PageSize;

    /// <summary>Null value when <paramref name="page"/> is null; an error for anything out of range.</summary>
    public static Result<PageRequest?> From(int? page, int? pageSize, string? sort, string? dir, IReadOnlySet<string> allowedSorts)
    {
        if (page is null) return Result<PageRequest?>.Success(null);
        if (page < 1) return Result<PageRequest?>.Failure("page must be 1 or more.");

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaxPageSize) return Result<PageRequest?>.Failure($"pageSize must be between 1 and {MaxPageSize}.");

        string? key = null;
        if (!string.IsNullOrWhiteSpace(sort))
        {
            key = sort.Trim().ToLowerInvariant();
            if (!allowedSorts.Contains(key))
                return Result<PageRequest?>.Failure($"sort must be one of: {string.Join(", ", allowedSorts)}.");
        }

        var descending = dir?.Trim().ToLowerInvariant() switch
        {
            null or "" or "asc" => false,
            "desc" => true,
            _ => (bool?)null
        };
        if (descending is null) return Result<PageRequest?>.Failure("dir must be asc or desc.");

        return Result<PageRequest?>.Success(new PageRequest(page.Value, size, key, descending.Value));
    }
}

/// <summary>One page of a list and the total number of matching rows.</summary>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Total);
