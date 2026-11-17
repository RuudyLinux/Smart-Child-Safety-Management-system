using System.Net;
using System.Text;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>A change to the RFID card list re-renders only the list region, so only the rows are fetched.</summary>
public class RfidListRequestTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public RfidListRequestTests(AdminWebFactory factory) => _factory = factory;

    private static HttpResponseMessage Api(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var json = path == "/api/rfid/cards/summary" ? "{\"total\":0,\"active\":0,\"lostOrDamaged\":0,\"availableSpare\":0,\"unassigned\":0}" : "[]";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (path == "/api/rfid/cards") response.Headers.Add("X-Total-Count", "0");
        return response;
    }

    [Fact]
    public async Task FullPage_FetchesRowsSummaryStudentsAndScanners()
    {
        var client = await _factory.SignedInClientAsync(Api);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(AdminWebFactory.Get("/Rfid?state=Lost", ajax: false))).StatusCode);
        var calls = _factory.Calls.Select(c => c.Split('?')[0]).ToList();
        foreach (var path in new[] { "/api/rfid/cards", "/api/rfid/cards/summary", "/api/students", "/api/rfid/devices" })
            Assert.Contains(path, calls);
        Assert.Contains(_factory.Calls, c => c.StartsWith("/api/rfid/cards?status=Lost&page=1&pageSize=25"));   // UI "state" -> API "status"
    }

    [Fact]
    public async Task ListRegionRefresh_FetchesOnlyTheRows()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Rfid?search=ab&assigned=no&page=3", ajax: true, regions: "list"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.Calls);
        Assert.StartsWith("/api/rfid/cards?search=ab&assigned=false&page=3&pageSize=25", call);
        Assert.Contains("data-cg-region=\"list\"", await response.Content.ReadAsStringAsync());
    }
}
