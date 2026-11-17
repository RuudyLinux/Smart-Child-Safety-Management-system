using ChildGuard.Application.Common;

namespace ChildGuard.Application.Tests;

public class PageRequestTests
{
    private static readonly IReadOnlySet<string> Sorts = new HashSet<string> { "name", "grade" };

    [Fact]
    public void From_NoPage_ReturnsNull()
    {
        var r = PageRequest.From(null, 50, "name", "desc", Sorts);
        Assert.True(r.Succeeded);
        Assert.Null(r.Value);
    }

    [Fact]
    public void From_Defaults_Page25AscendingNoSort()
    {
        var r = PageRequest.From(2, null, null, null, Sorts).Value!;
        Assert.Equal(2, r.Page);
        Assert.Equal(PageRequest.DefaultPageSize, r.PageSize);
        Assert.Null(r.Sort);
        Assert.False(r.Descending);
        Assert.Equal(25, r.Skip);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void From_OutOfRange_IsError(int page, int size) =>
        Assert.False(PageRequest.From(page, size, null, null, Sorts).Succeeded);

    [Fact]
    public void From_PageSize100_IsAllowed() =>
        Assert.Equal(100, PageRequest.From(1, 100, null, null, Sorts).Value!.PageSize);

    [Fact]
    public void From_UnknownSort_IsError()
    {
        var r = PageRequest.From(1, 25, "PasswordHash", null, Sorts);
        Assert.False(r.Succeeded);
        Assert.Contains("name, grade", r.Error);
    }

    [Theory]
    [InlineData("desc", true)]
    [InlineData("DESC", true)]
    [InlineData("asc", false)]
    public void From_Direction(string dir, bool descending) =>
        Assert.Equal(descending, PageRequest.From(1, 25, "Name", dir, Sorts).Value!.Descending);

    [Fact]
    public void From_BadDirection_IsError() =>
        Assert.False(PageRequest.From(1, 25, "name", "sideways", Sorts).Succeeded);

    [Fact]
    public void From_SortIsNormalisedToLowerCase() =>
        Assert.Equal("name", PageRequest.From(1, 25, "Name", null, Sorts).Value!.Sort);
}
