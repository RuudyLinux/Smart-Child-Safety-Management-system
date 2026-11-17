using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Optional server paging on the bus assignment list (every assignment ever made, so it only grows). Without
/// <c>page</c> the endpoint answers as before; with it, one page of the same rows plus the total in X-Total-Count.
/// </summary>
public class PagedAssignmentsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public PagedAssignmentsTests(ApiFactory api) => _api = api;

    private static string Uid(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static async Task<BusDto> BusAsync(HttpClient admin, string number, string routeName)
    {
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(routeName, null), Json.Options));
        return await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(number, 40, null, route.RouteId), Json.Options));
    }

    private static async Task<int> StudentAsync(HttpClient admin, string name) =>
        (await Ok<StudentDetailDto>(await admin.PostAsJsonAsync("/api/students", new CreateStudentRequest(name, new DateTime(2016, 4, 21), "G-asg"), Json.Options))).StudentId;

    private static Task<HttpResponseMessage> Assign(HttpClient admin, int studentId, int busId) =>
        admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(studentId, busId, null), Json.Options);

    private static async Task<(List<StudentBusAssignmentDto> Items, int? Total)> GetAsync(HttpClient admin, string query)
    {
        var r = await admin.GetAsync("/api/transportation/assignments?" + query);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        int? total = r.Headers.TryGetValues("X-Total-Count", out var v) ? int.Parse(v.Single()) : null;
        return ((await r.Content.ReadFromJsonAsync<List<StudentBusAssignmentDto>>(Json.Options))!, total);
    }

    [Fact]
    public async Task WithoutPage_AnswersAsBefore_NoTotalHeader()
    {
        var admin = await _api.AdminClientAsync();
        var bus = await BusAsync(admin, Uid("BUSA"), Uid("RouteA"));
        await Assign(admin, await StudentAsync(admin, "Asg Unpaged"), bus.BusId);
        var r = await admin.GetAsync("/api/transportation/assignments");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False(r.Headers.Contains("X-Total-Count"));
        Assert.Contains((await r.Content.ReadFromJsonAsync<List<StudentBusAssignmentDto>>(Json.Options))!, a => a.StudentName == "Asg Unpaged");
    }

    [Fact]
    public async Task Paged_SlicesAndCounts_NewestFirstByDefault()
    {
        var admin = await _api.AdminClientAsync();
        var number = Uid("BUSP");
        var bus = await BusAsync(admin, number, Uid("RouteP"));
        for (var i = 1; i <= 5; i++)
        {
            await Assign(admin, await StudentAsync(admin, $"Asgpage {i}"), bus.BusId);
            _api.Time.Advance(TimeSpan.FromMinutes(1));   // distinct assignment times
        }
        var (page1, total) = await GetAsync(admin, $"busId={bus.BusId}&page=1&pageSize=2");
        var (page3, _) = await GetAsync(admin, $"busId={bus.BusId}&page=3&pageSize=2");
        Assert.Equal(5, total);
        Assert.Equal(new[] { "Asgpage 5", "Asgpage 4" }, page1.Select(a => a.StudentName));   // newest first
        Assert.Equal(new[] { "Asgpage 1" }, page3.Select(a => a.StudentName));
    }

    [Fact]
    public async Task Filters_SearchByStudentBusOrRoute_AndStatus()
    {
        var admin = await _api.AdminClientAsync();
        var routeName = Uid("RouteF");
        var bus = await BusAsync(admin, Uid("BUSF"), routeName);
        var kept = await StudentAsync(admin, "Asgflt Kept");
        var moved = await StudentAsync(admin, "Asgflt Moved");
        var other = await BusAsync(admin, Uid("BUSG"), Uid("RouteG"));
        await Assign(admin, kept, bus.BusId);
        await Assign(admin, moved, bus.BusId);
        await Assign(admin, moved, other.BusId);          // ends the first assignment of "moved"

        Assert.Equal(1, (await GetAsync(admin, "search=Asgflt%20Kept&page=1")).Total);                                  // student
        Assert.Equal(2, (await GetAsync(admin, $"search={bus.BusNumber}&page=1")).Total);                               // bus: kept + moved's ended one
        Assert.Equal(2, (await GetAsync(admin, $"search={routeName}&page=1")).Total);                                   // the bus's own route
        Assert.Equal(1, (await GetAsync(admin, $"search={bus.BusNumber}&status=Ended&page=1")).Total);                  // moved's old one
        Assert.Equal(1, (await GetAsync(admin, $"search={bus.BusNumber}&status=Active&page=1")).Total);                 // kept
        Assert.Equal(1, (await GetAsync(admin, $"busId={other.BusId}&page=1")).Total);
    }

    [Fact]
    public async Task Sorts_ByStudentBusAndStatus_WithDirection()
    {
        var admin = await _api.AdminClientAsync();
        var bus = await BusAsync(admin, Uid("BUSS"), Uid("RouteS"));
        foreach (var n in new[] { "Asgsort Charlie", "Asgsort alpha", "Asgsort Bravo" })
            await Assign(admin, await StudentAsync(admin, n), bus.BusId);
        var (asc, _) = await GetAsync(admin, "search=Asgsort&page=1&sort=student&dir=asc");
        Assert.Equal(new[] { "Asgsort alpha", "Asgsort Bravo", "Asgsort Charlie" }, asc.Select(a => a.StudentName));
        var (desc, _) = await GetAsync(admin, "search=Asgsort&page=1&sort=student&dir=desc");
        Assert.Equal(new[] { "Asgsort Charlie", "Asgsort Bravo", "Asgsort alpha" }, desc.Select(a => a.StudentName));
    }

    [Theory]
    [InlineData("page=1&sort=studentId")]
    [InlineData("page=0")]
    [InlineData("page=1&pageSize=101")]
    [InlineData("page=1&status=Deleted")]
    public async Task BadPaging_Is400(string query)
    {
        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/transportation/assignments?" + query)).StatusCode);
    }

    [Fact]
    public async Task RequiresAdmin()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/transportation/assignments?page=1")).StatusCode);
    }
}
