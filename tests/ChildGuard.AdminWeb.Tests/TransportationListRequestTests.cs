using System.Net;
using System.Text;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>
/// The fleet console: a change to the assignments list re-renders only the list region, which needs the
/// buses, routes and settings but not the drivers, the student list or the audit; a full page needs all.
/// </summary>
public class TransportationListRequestTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public TransportationListRequestTests(AdminWebFactory factory) => _factory = factory;

    private static HttpResponseMessage Api(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var json = path == "/api/transportation/settings" ? "{\"allowRouteOverride\":false}" : "[]";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (path == "/api/transportation/assignments") response.Headers.Add("X-Total-Count", "0");
        return response;
    }

    [Fact]
    public async Task FullPage_FetchesEverything()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Transportation?search=bus", ajax: false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = _factory.Calls.Select(c => c.Split('?')[0]).ToList();
        foreach (var path in new[] { "/api/transportation/buses", "/api/transportation/routes", "/api/transportation/settings",
                     "/api/transportation/assignments", "/api/transportation/drivers", "/api/students", "/api/transportation/audit" })
            Assert.Contains(path, calls);
    }

    [Fact]
    public async Task ListRegionRefresh_SkipsDriversStudentsAndAudit()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Transportation?search=bus&page=2", ajax: true, regions: "list"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var calls = _factory.Calls.Select(c => c.Split('?')[0]).ToList();
        Assert.DoesNotContain("/api/transportation/drivers", calls);
        Assert.DoesNotContain("/api/students", calls);
        Assert.DoesNotContain("/api/transportation/audit", calls);
        Assert.Single(calls, c => c == "/api/transportation/assignments");
        Assert.Contains(_factory.Calls, c => c.StartsWith("/api/transportation/assignments?search=bus&page=2&pageSize=25"));
        Assert.Contains("data-cg-region=\"list\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConsoleRegionRefresh_GetsTheWholePage()
    {
        var client = await _factory.SignedInClientAsync(Api);
        await client.SendAsync(AdminWebFactory.Get("/Transportation", ajax: true, regions: "fleet-console"));
        Assert.Contains("/api/transportation/drivers", _factory.Calls.Select(c => c.Split('?')[0]));
    }
}
