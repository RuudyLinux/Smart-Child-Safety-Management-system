namespace ChildGuard.AdminWeb.Models.ViewModels;

/// <summary>
/// A sortable column header for a server-paged list. <paramref name="Url"/> builds the list URL for
/// (page, sort, dir); the header links to its own sort, flipping the direction when it is already active.
/// </summary>
public sealed record SortHeaderModel(string Key, string Label, string? ActiveKey, bool Descending,
    Func<int?, string?, string?, string> Url)
{
    public bool Active => string.Equals(Key, ActiveKey, StringComparison.Ordinal);
    public string NextDir => Active && !Descending ? "desc" : "asc";
    public string Href => Url(null, Key, NextDir);
}

/// <summary>Pager for a server-paged list: real links (work without JavaScript), a window around the current page.</summary>
public sealed record PagerModel(string Scope, int Page, int PageCount, int FirstRow, int LastRow, int Total, string Noun,
    Func<int, string> Url)
{
    /// <summary>Page numbers to show; 0 marks a gap ("…"). At most 1, current±2 and the last page.</summary>
    public IReadOnlyList<int> Pages
    {
        get
        {
            if (PageCount < 2) return Array.Empty<int>();
            var shown = new SortedSet<int> { 1, PageCount };
            for (var p = Page - 2; p <= Page + 2; p++) if (p >= 1 && p <= PageCount) shown.Add(p);
            var list = new List<int>();
            var previous = 0;
            foreach (var p in shown)
            {
                if (previous != 0 && p > previous + 1) list.Add(0);
                list.Add(p);
                previous = p;
            }
            return list;
        }
    }
}
