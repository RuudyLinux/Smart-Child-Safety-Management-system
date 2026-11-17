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

    [Fact]
    public async Task OtherRegionRefresh_StillGetsTheWholePage()
    {
        // Only a request for exactly the list region may skip the rest of the page.
        var client = await _factory.SignedInClientAsync(Api);
        await client.SendAsync(AdminWebFactory.Get("/Students", ajax: true, regions: "list,other"));
        Assert.Contains("/api/students/grades", _factory.Calls);
    }
}
