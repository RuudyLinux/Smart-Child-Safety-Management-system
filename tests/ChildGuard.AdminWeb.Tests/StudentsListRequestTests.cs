using System.Net;
using System.Text;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// A list change (typing, a filter, a page) re-renders only the list region, so AdminWeb asks the API only for
/// the rows; the header figures and grade options are fetched for a full page load.
/// </summary>
public class StudentsListRequestTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public StudentsListRequestTests(AdminWebFactory factory) => _factory = factory;

    private static HttpResponseMessage Api(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var json = path == "/api/students/grades" ? "[\"5A\"]" : "[]";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (path == "/api/students") response.Headers.Add("X-Total-Count", "0");
        return response;
    }

    [Fact]
    public async Task FullPage_FetchesRowsGradesAndFigures()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Students?search=mira", ajax: false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = _factory.Calls.ToList();
        Assert.Contains("/api/students/grades", calls);
        Assert.Equal(5, calls.Count(c => c.StartsWith("/api/students?")));   // rows + 4 figures
    }

    [Fact]
    public async Task ListRegionRefresh_FetchesOnlyTheRows()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Students?search=mira&page=2", ajax: true, regions: "list"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.Calls);
        Assert.StartsWith("/api/students?search=mira&page=2&pageSize=25", call);
        Assert.Contains("data-cg-region=\"list\"", await response.Content.ReadAsStringAsync());
    }

    /// <summary>The API holds 60 matching students; whatever page is asked for, it answers with no rows.</summary>
    private static HttpResponseMessage SixtyButEmptyPage(HttpRequestMessage r)
    {
        var response = Api(r);
        if (r.RequestUri!.AbsolutePath == "/api/students") { response.Headers.Remove("X-Total-Count"); response.Headers.Add("X-Total-Count", "60"); }
        return response;
    }

    [Fact]
    public async Task PageBeyondTheEnd_SaysSoAndLinksToTheLastPage()
    {
        var client = await _factory.SignedInClientAsync(SixtyButEmptyPage);
        var html = await (await client.SendAsync(AdminWebFactory.Get("/Students?search=mira&page=9", ajax: true, regions: "list"))).Content.ReadAsStringAsync();
        Assert.Contains("There is no page 9", html);
        Assert.Contains("href=\"/Students?search=mira&amp;page=3\"", html);   // 60 rows = 3 pages
        Assert.DoesNotContain("Showing 201", html);
    }

    [Fact]
    public async Task HugePage_IsClampedBeforeTheApiIsAsked()
    {
        var client = await _factory.SignedInClientAsync(SixtyButEmptyPage);
        var response = await client.SendAsync(AdminWebFactory.Get("/Students?page=2147483647", ajax: true, regions: "list"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = int.Parse(System.Text.RegularExpressions.Regex.Match(Assert.Single(_factory.Calls), @"[?&]page=(\d+)").Groups[1].Value);
        Assert.True((long)(page - 1) * 25 <= int.MaxValue, $"page {page} would overflow the API's offset");
        Assert.Contains("There is no page", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyTheCountIsAnnounced_NotTheWholeTable()
    {
        var client = await _factory.SignedInClientAsync(SixtyButEmptyPage);
        var html = await (await client.SendAsync(AdminWebFactory.Get("/Students?page=1", ajax: false))).Content.ReadAsStringAsync();
        var region = System.Text.RegularExpressions.Regex.Match(html, "<div[^>]*data-cg-region=\"list\"[^>]*>").Value;
        Assert.DoesNotContain("aria-live", region);
        // The announcer lives outside the refreshed region: a live region that is replaced already filled is
        // announced unreliably, so cg-ajax.js copies the new count into this one after each list change.
        var announcer = System.Text.RegularExpressions.Regex.Match(html, "<[^>]*data-cg-list-status[^>]*>").Value;
        Assert.Contains("aria-live=\"polite\"", announcer);
        Assert.True(html.IndexOf("data-cg-list-status", StringComparison.Ordinal) < html.IndexOf("data-cg-region=\"list\"", StringComparison.Ordinal),
            "the announcer must sit outside (before) the list region");
    }

    [Fact]
    public async Task OtherRegionRefresh_StillGetsTheWholePage()
    {
        // Only a request for exactly the list region may skip the rest of the page.
        var client = await _factory.SignedInClientAsync(Api);
        await client.SendAsync(AdminWebFactory.Get("/Students", ajax: true, regions: "list,other"));
        Assert.Contains("/api/students/grades", _factory.Calls);
    }
}
