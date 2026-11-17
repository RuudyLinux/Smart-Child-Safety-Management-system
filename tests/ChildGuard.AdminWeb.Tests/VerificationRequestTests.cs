using System.Net;
using System.Text;

namespace ChildGuard.AdminWeb.Tests;

/// <summary>The verification log asks the API for one page, and for the header figures and student picker only on a full page.</summary>
public class VerificationRequestTests : IClassFixture<AdminWebFactory>
{
    private readonly AdminWebFactory _factory;
    public VerificationRequestTests(AdminWebFactory factory) => _factory = factory;

    private static HttpResponseMessage Api(HttpRequestMessage r)
    {
        var path = r.RequestUri!.AbsolutePath;
        var json = path == "/api/reports/verification/summary" ? "{\"total\":0,\"matched\":0,\"noMatch\":0,\"approved\":0}" : "[]";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (path == "/api/reports/verification") response.Headers.Add("X-Total-Count", "0");
        return response;
    }

    [Fact]
    public async Task FullPage_AsksForARowsPage_TheSummaryForTheSameStudentAndDates_AndTheStudents()
    {
        var client = await _factory.SignedInClientAsync(Api);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(AdminWebFactory.Get("/Verification/History?studentId=7&from=2026-11-01&result=Match&search=ab", ajax: false))).StatusCode);
        Assert.Contains(_factory.Calls, c => c == "/api/reports/verification?search=ab&studentId=7&result=Match&from=2026-11-01&page=1&pageSize=50"
                                          || c.StartsWith("/api/reports/verification?") && c.Contains("studentId=7") && c.Contains("result=Match") && c.Contains("pageSize=50"));
        // The header figures cover the student and the dates, not the search or the result filter.
        Assert.Contains("/api/reports/verification/summary?studentId=7&from=2026-11-01", _factory.Calls);
        Assert.Contains("/api/students", _factory.Calls.Select(c => c.Split('?')[0]));
    }

    [Fact]
    public async Task ListRegionRefresh_AsksOnlyForTheRows()
    {
        var client = await _factory.SignedInClientAsync(Api);
        var response = await client.SendAsync(AdminWebFactory.Get("/Verification/History?page=2&handover=not", ajax: true, regions: "list"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.Calls);
        Assert.StartsWith("/api/reports/verification?approved=false&page=2&pageSize=50", call);
    }

    [Fact]
    public async Task InvalidIdAndDate_NeverReachTheApi()
    {
        var client = await _factory.SignedInClientAsync(Api);
        await client.SendAsync(AdminWebFactory.Get("/Verification/History?studentId=abc&from=nonsense&page=99999999999999", ajax: false));
        var rows = _factory.Calls.First(c => c.StartsWith("/api/reports/verification?"));
        Assert.DoesNotContain("studentId", rows);
        Assert.DoesNotContain("from=", rows);
        Assert.Contains("page=1&", rows);
    }
}
