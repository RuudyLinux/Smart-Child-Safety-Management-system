using ChildGuard.AdminWeb.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// The shared server-paged list view: the page URL (what the admin sees and bookmarks) maps onto the API's
/// paging query for any list. Unknown choices are no filter, unknown sorts are dropped, pages are clamped.
/// </summary>
public class ListQueryTests
{
    private static readonly ListDefinition Guardians = new(
        BasePath: "/Guardians",
        ApiPath: "/api/guardians",
        Sorts: new HashSet<string> { "name", "children", "activity" },
        Filters: new[]
        {
            ListFilter.Choice("verification", "faceReady", ("ready", "true"), ("missing", "false")),
            ListFilter.Choice("children", "hasStudents", ("linked", "true"), ("none", "false")),
            ListFilter.Choice("status", "status", ("Active", "Active"), ("Inactive", "Inactive"), ("Archived", "Archived")),
            ListFilter.Text("grade", "grade"),
        },
        SortAliases: new Dictionary<string, string> { ["children"] = "students" });

    private static ListQuery Q(params (string Key, string Value)[] pairs) =>
        new(Guardians, new QueryCollection(pairs.ToDictionary(p => p.Key, p => new StringValues(p.Value))));

    [Fact]
    public void Defaults_FirstPage()
    {
        var q = Q();
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
        Assert.Equal(1, q.CurrentPage);
        Assert.False(q.HasFilters);
    }

    [Fact]
    public void UiValues_MapToApiFilters_AndSortAliasesApply()
    {
        var q = Q(("search", "Mira Shah"), ("verification", "ready"), ("children", "none"), ("status", "Archived"),
            ("grade", "5A"), ("sort", "children"), ("dir", "desc"), ("page", "3"));
        Assert.Equal(
            "search=Mira%20Shah&faceReady=true&hasStudents=false&status=Archived&grade=5A&page=3&pageSize=25&sort=students&dir=desc",
            q.ToApiQuery());
        Assert.True(q.HasFilters);
    }

    [Theory]
    [InlineData("__all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void AnyOrUnknownChoice_IsNoFilter(string value)
    {
        var q = Q(("verification", value), ("children", value), ("status", value));
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
        Assert.False(q.HasFilters);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-4", 1)]
    [InlineData("abc", 1)]
    [InlineData("2", 2)]
    [InlineData("2147483647", ListQuery.MaxPage)]
    [InlineData("99999999999999", 1)]   // beyond int: not a page number at all
    public void Page_IsClamped(string raw, int expected) => Assert.Equal(expected, Q(("page", raw)).CurrentPage);

    [Theory]
    [InlineData("12", "studentId=12&", true)]
    [InlineData("abc", "", false)]
    [InlineData("-3", "", false)]
    [InlineData("1; DROP", "", false)]
    public void TextFilterWithAValidator_IgnoresValuesItRefuses(string raw, string expectedPart, bool keeps)
    {
        var def = new ListDefinition("/V", "/api/v", new HashSet<string> { "when" },
            new[] { ListFilter.Text("studentId", "studentId", v => int.TryParse(v, System.Globalization.NumberStyles.None, null, out _)) });
        var q = new ListQuery(def, new QueryCollection(new Dictionary<string, StringValues> { ["studentId"] = raw }));
        Assert.Equal(keeps, q.HasFilters);
        Assert.Equal($"{expectedPart}page=1&pageSize=25", q.ToApiQuery());
        Assert.Equal(keeps ? "12" : null, q.Value("studentId"));
    }

    [Fact]
    public void UnknownSortOrDirection_IsDropped()
    {
        var q = Q(("sort", "PasswordHash"), ("dir", "sideways"));
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
        Assert.Null(q.SortKey);
        Assert.False(q.Descending);
    }

    [Fact]
    public void Url_KeepsFiltersAndSort_NewSortStartsAtPageOne()
    {
        var q = Q(("search", "x"), ("children", "linked"), ("sort", "name"), ("dir", "asc"), ("page", "2"));
        Assert.Equal("/Guardians?search=x&children=linked&sort=name&dir=asc&page=4", q.Url(page: 4));
        Assert.Equal("/Guardians?search=x&children=linked&sort=activity&dir=desc", q.Url(sort: "activity", dir: "desc"));
        Assert.Equal("/Guardians?search=x&children=linked&sort=name&dir=asc", q.Url(page: 1));
    }

    [Fact]
    public void Url_DropsBogusValues()
    {
        var q = Q(("children", "bogus"), ("grade", "__all"), ("search", "  "));
        Assert.Equal("/Guardians", q.Url());
    }

    [Fact]
    public void ClearedFilters_KeepsSearchAndSort()
    {
        var q = Q(("search", "mira"), ("children", "none"), ("status", "Active"), ("sort", "name"), ("dir", "desc"), ("page", "5"));
        Assert.Equal("/Guardians?search=mira&sort=name&dir=desc", q.ClearedFilters().Url());
    }

    [Fact]
    public void Value_ReturnsTheRawChoice_ForSelectingOptions()
    {
        var q = Q(("children", "none"), ("verification", "nonsense"));
        Assert.Equal("none", q.Value("children"));
        Assert.Null(q.Value("verification"));   // an unknown choice selects nothing
        Assert.Null(q.Value("grade"));
    }
}
