using Microsoft.AspNetCore.Http;

namespace ChildGuard.AdminWeb.Models.ViewModels;

/// <summary>
/// One filter of a server-paged list. <c>Name</c> is the page URL and form field name; <c>ApiName</c> the API
/// query parameter. A choice filter maps the select's values to API values (anything else is "no filter");
/// a text filter passes what was typed or picked through.
/// </summary>
public sealed record ListFilter(string Name, string ApiName, IReadOnlyDictionary<string, string>? Map)
{
    public static ListFilter Choice(string name, string apiName, params (string Ui, string Api)[] choices) =>
        new(name, apiName, choices.ToDictionary(c => c.Ui, c => c.Api, StringComparer.Ordinal));

    public static ListFilter Text(string name, string apiName) => new(name, apiName, null);
}

/// <summary>What one server-paged list is: its page, its API endpoint, its sorts and filters.</summary>
/// <param name="SortAliases">Page sort name to API sort name, where they differ.</param>
public sealed record ListDefinition(
    string BasePath,
    string ApiPath,
    IReadOnlySet<string> Sorts,
    IReadOnlyList<ListFilter> Filters,
    IReadOnlyDictionary<string, string>? SortAliases = null,
    int PageSize = 25);

/// <summary>
/// A server-paged list as it appears in the page URL (bookmarkable; Back and Refresh restore it), for any list.
/// Unknown choices are no filter, unknown sorts are dropped and the page is clamped, so a hand-edited URL can
/// never reach the API as something it refuses.
/// </summary>
public sealed class ListQuery
{
    /// <summary>Highest page whose row offset fits the API's limit at the largest page size.</summary>
    public const int MaxPage = int.MaxValue / 100;

    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);

    public ListDefinition Definition { get; }
    public string? Search { get; }
    public string? Sort { get; }
    public bool Descending { get; }
    private readonly int _page;

    public ListQuery(ListDefinition definition, IQueryCollection query)
        : this(definition, Read(query, "search"), Read(query, "sort"), Read(query, "dir"), Read(query, "page"),
            definition.Filters.Select(f => (f.Name, Read(query, f.Name))))
    { }

    private ListQuery(ListDefinition definition, string? search, string? sort, string? dir, string? page,
        IEnumerable<(string Name, string? Value)> filterValues)
    {
        Definition = definition;
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        Sort = sort is not null && definition.Sorts.Contains(sort) ? sort : null;
        Descending = string.Equals(dir, "desc", StringComparison.Ordinal);
        _page = int.TryParse(page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 1;
        foreach (var (name, raw) in filterValues)
        {
            var filter = definition.Filters.First(f => f.Name == name);
            var value = string.IsNullOrWhiteSpace(raw) || raw == "__all" ? null : raw.Trim();
            if (value is null) continue;
            if (filter.Map is not null && !filter.Map.ContainsKey(value)) continue;   // an unknown choice selects nothing
            _filters[name] = value;
        }
    }

    private static string? Read(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

    public int CurrentPage => _page > 1 ? Math.Min(_page, MaxPage) : 1;
    public string? SortKey => Sort;
    public int PageSize => Definition.PageSize;

    /// <summary>The raw choice or text for a filter (to select the option); null when none or unknown.</summary>
    public string? Value(string name) => _filters.TryGetValue(name, out var v) ? v : null;

    /// <summary>True when a search or any filter narrows the list (sort and page do not).</summary>
    public bool HasFilters => Search is not null || _filters.Count > 0;

    /// <summary>The API query for this view: always paged, filters and sort translated.</summary>
    public string ToApiQuery()
    {
        var p = new List<string>();
        Add(p, "search", Search);
        foreach (var f in Definition.Filters)
            if (_filters.TryGetValue(f.Name, out var v)) Add(p, f.ApiName, f.Map is null ? v : f.Map[v]);
        p.Add($"page={CurrentPage}");
        p.Add($"pageSize={PageSize}");
        if (Sort is { } sort)
        {
            p.Add($"sort={(Definition.SortAliases is not null && Definition.SortAliases.TryGetValue(sort, out var alias) ? alias : sort)}");
            p.Add($"dir={(Descending ? "desc" : "asc")}");
        }
        return string.Join("&", p);
    }

    /// <summary>
    /// This list's page URL with one thing changed: another page keeps sort and filters; another sort starts at
    /// page 1. Used for the pager and sort headers, which are plain links (they work without JavaScript).
    /// </summary>
    public string Url(int? page = null, string? sort = null, string? dir = null)
    {
        var p = new List<string>();
        Add(p, "search", Search);
        foreach (var f in Definition.Filters)
            if (_filters.TryGetValue(f.Name, out var v)) Add(p, f.Name, v);
        var s = sort ?? Sort;
        if (s is not null)
        {
            Add(p, "sort", s);
            Add(p, "dir", sort is not null ? (dir ?? "asc") : (Descending ? "desc" : "asc"));
        }
        if (sort is null && page is > 1) p.Add($"page={page}");
        return Definition.BasePath + (p.Count == 0 ? "" : "?" + string.Join("&", p));
    }

    /// <summary>The same list with its filters cleared; the search typed so far and the sort are kept.</summary>
    public ListQuery ClearedFilters() =>
        new(Definition, Search, Sort, Descending ? "desc" : "asc", null, Definition.Filters.Select(f => (f.Name, (string?)null)));

    private static void Add(List<string> p, string name, string? value)
    {
        if (value is not null) p.Add($"{name}={Uri.EscapeDataString(value)}");
    }
}
