using ChildGuard.AdminWeb.Models.ViewModels;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>The Students list's URL (what the admin sees and bookmarks) maps onto the API's paging query.</summary>
public class StudentListQueryTests
{
    [Fact]
    public void Defaults_FirstPageByName()
    {
        var q = new StudentListQuery();
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
        Assert.Equal(1, q.CurrentPage);
    }

    [Fact]
    public void UiValues_MapToApiFilters()
    {
        var q = new StudentListQuery
        {
            Search = "Mira Shah", Grade = "5A", Attendance = "present", Rfid = "none", Guardian = "linked",
            Status = "Archived", Sort = "activity", Dir = "desc", Page = 3
        };
        Assert.Equal(
            "search=Mira%20Shah&grade=5A&status=Archived&hasRfid=false&hasGuardian=true&presentToday=true&page=3&pageSize=25&sort=activity&dir=desc",
            q.ToApiQuery());
    }

    [Theory]
    [InlineData("__all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void AnyOrUnknownChoice_IsNoFilter(string value)
    {
        var q = new StudentListQuery { Attendance = value, Rfid = value, Guardian = value, Status = value, Grade = value == "bogus" ? null : value };
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageBelowOne_IsFirstPage(int page) =>
        Assert.Equal(1, new StudentListQuery { Page = page }.CurrentPage);

    [Fact]
    public void UnknownSortOrDirection_IsDropped()
    {
        var q = new StudentListQuery { Sort = "PasswordHash", Dir = "sideways" };
        Assert.Equal("page=1&pageSize=25", q.ToApiQuery());
        Assert.Null(q.SortKey);
        Assert.False(q.Descending);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("__all", "__all", false)]
    [InlineData("  ", null, false)]
    [InlineData("mira", null, true)]
    [InlineData(null, "none", true)]
    public void HasFilters_OnlyForRealChoices(string? search, string? rfid, bool expected) =>
        Assert.Equal(expected, new StudentListQuery { Search = search, Rfid = rfid, Sort = "name", Page = 3 }.HasFilters);

    [Fact]
    public void WithPage_And_WithSort_KeepFilters()
    {
        var q = new StudentListQuery { Search = "x", Rfid = "assigned", Sort = "name", Dir = "asc", Page = 2 };
        Assert.Equal("/Students?search=x&rfid=assigned&sort=name&dir=asc&page=4", q.Url(page: 4));
        // A new sort starts again at page 1.
        Assert.Equal("/Students?search=x&rfid=assigned&sort=grade&dir=desc", q.Url(sort: "grade", dir: "desc"));
    }
}
