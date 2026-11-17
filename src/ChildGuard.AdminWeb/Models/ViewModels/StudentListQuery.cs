namespace ChildGuard.AdminWeb.Models.ViewModels;

/// <summary>
/// The Students list as it appears in the URL (bookmarkable; Back and Refresh restore it). Choice values are the
/// ones the list's selects already used ("present", "none", "linked"…; "__all" or anything unknown = no filter),
/// so the page reads the same with or without JavaScript. Bound from the query string.
/// </summary>
public sealed class StudentListQuery
{
    public const int PageSize = 25;
    private static readonly HashSet<string> Sorts = new(StringComparer.Ordinal) { "name", "grade", "activity", "status" };
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "Active", "Inactive", "Archived" };

    public string? Search { get; set; }
    public string? Grade { get; set; }
    public string? Attendance { get; set; }   // present | absent
    public string? Rfid { get; set; }         // assigned | none
    public string? Guardian { get; set; }     // linked | none
    public string? Status { get; set; }       // Active | Inactive | Archived
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public int? Page { get; set; }

    /// <summary>Highest page whose row offset still fits the API's limit; anything beyond is clamped to it.</summary>
    public const int MaxPage = int.MaxValue / PageSize;

    public int CurrentPage => Page is > 1 ? Math.Min(Page.Value, MaxPage) : 1;
    public string? SortKey => Sort is not null && Sorts.Contains(Sort) ? Sort : null;
    public bool Descending => string.Equals(Dir, "desc", StringComparison.Ordinal);

    private static string? Text(string? v) => string.IsNullOrWhiteSpace(v) || v == "__all" ? null : v.Trim();
    private static bool? Choice(string? v, string yes, string no) => v == yes ? true : v == no ? false : null;

    private string? StatusValue => Status is not null && Statuses.Contains(Status) ? Status : null;

    /// <summary>True when any search or filter narrows the list (sort and page do not).</summary>
    public bool HasFilters =>
        Text(Search) is not null || Text(Grade) is not null || StatusValue is not null
        || Choice(Attendance, "present", "absent") is not null || Choice(Rfid, "assigned", "none") is not null
        || Choice(Guardian, "linked", "none") is not null;

    /// <summary>The API query for this view: always paged (25 a page), filters and sort translated.</summary>
    public string ToApiQuery()
    {
        var p = new List<string>();
        Add(p, "search", Text(Search));
        Add(p, "grade", Text(Grade));
        Add(p, "status", StatusValue);
        Add(p, "hasRfid", Bool(Choice(Rfid, "assigned", "none")));
        Add(p, "hasGuardian", Bool(Choice(Guardian, "linked", "none")));
        Add(p, "presentToday", Bool(Choice(Attendance, "present", "absent")));
        p.Add($"page={CurrentPage}");
        p.Add($"pageSize={PageSize}");
        if (SortKey is { } sort)
        {
            p.Add($"sort={sort}");
            p.Add($"dir={(Descending ? "desc" : "asc")}");
        }
        return string.Join("&", p);
    }

    /// <summary>
    /// This list's page URL with one thing changed: another page keeps sort and filters; another sort starts at
    /// page 1. Used for the pager and sort headers, which work as plain links without JavaScript.
    /// </summary>
    public string Url(int? page = null, string? sort = null, string? dir = null)
    {
        var p = new List<string>();
        Add(p, "search", Text(Search));
        Add(p, "grade", Text(Grade));
        Add(p, "attendance", Choice(Attendance, "present", "absent") is null ? null : Attendance);
        Add(p, "rfid", Choice(Rfid, "assigned", "none") is null ? null : Rfid);
        Add(p, "guardian", Choice(Guardian, "linked", "none") is null ? null : Guardian);
        Add(p, "status", StatusValue);
        var s = sort ?? SortKey;
        if (s is not null)
        {
            Add(p, "sort", s);
            Add(p, "dir", sort is not null ? (dir ?? "asc") : (Descending ? "desc" : "asc"));
        }
        if (sort is null && page is > 1) p.Add($"page={page}");
        return "/Students" + (p.Count == 0 ? "" : "?" + string.Join("&", p));
    }

    private static string? Bool(bool? v) => v is null ? null : v.Value ? "true" : "false";

    private static void Add(List<string> p, string name, string? value)
    {
        if (value is not null) p.Add($"{name}={Uri.EscapeDataString(value)}");
    }
}

/// <summary>School-wide figures in the Students page header (null when the API could not be asked).</summary>
public sealed record StudentListFigures(int? OnRoll, int? PresentToday, int? WithRfid, int? WithoutGuardian);

/// <summary>One page of a server-paged list, plus what the pager and sort headers need.</summary>
public sealed record ListPageViewModel<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int PageCount => Total == 0 ? 1 : (Total + PageSize - 1) / PageSize;
    public int FirstRow => Total == 0 ? 0 : (int)Math.Min(int.MaxValue, (long)(Page - 1) * PageSize + 1);
    public int LastRow => (int)Math.Min(Total, (long)Page * PageSize);

    /// <summary>A page after the last one (a stale bookmark, or Back after records were removed).</summary>
    public bool PastTheEnd => Items.Count == 0 && Total > 0 && Page > PageCount;
}
