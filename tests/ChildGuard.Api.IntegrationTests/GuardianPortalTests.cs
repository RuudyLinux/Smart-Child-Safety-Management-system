using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>The Guardian app's read endpoints: dashboard, student details, transport, reports, pickups.</summary>
public class GuardianPortalTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public GuardianPortalTests(ApiFactory api) => _api = api;

    private async Task<T> GetOkAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    [Fact]
    public async Task Dashboard_ShowsOwnChildrenWithTodaysState()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);

        var dash = await GetOkAsync<GuardianDashboardDto>(_api.GuardianClient(child.GuardianId), "/api/me/dashboard");

        var card = Assert.Single(dash.Children);
        Assert.Equal(child.StudentId, card.StudentId);
        Assert.Equal(StudentSafetyStatus.AtSchool, card.SafetyStatus);
        Assert.True(card.PresentToday);
        Assert.Equal(1, dash.AtSchool);
        Assert.Equal(1, dash.PresentToday);
        Assert.Contains(dash.RecentEvents, e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.SchoolArrival);
        Assert.Equal(_api.Clock.Today, dash.Today);
    }

    [Fact]
    public async Task Students_ListsOnlyLinkedChildren()
    {
        var mine = await _api.NewChildAsync();
        await _api.NewChildAsync();

        var list = await GetOkAsync<List<GuardianChildCardDto>>(_api.GuardianClient(mine.GuardianId), "/api/me/students");

        Assert.Equal(mine.StudentId, Assert.Single(list).StudentId);
    }

    [Fact]
    public async Task StudentDetail_IncludesTransportAndCoGuardians_WithoutContactDetails()
    {
        var child = await _api.NewChildAsync();
        int coGuardianId;
        await using (var db = _api.NewDb())
        {
            var driver = new Driver { FullName = "Driver Dan", Phone = "9000000001" };
            var route = new Route { RouteName = $"Route {Guid.NewGuid():N}"[..12], Description = "North loop" };
            var bus = new Bus { BusNumber = $"B{Guid.NewGuid():N}"[..8], Capacity = 40, Driver = driver, Route = route };
            var co = new Guardian { FullName = "Co Guardian", Email = $"co.{Guid.NewGuid():N}@childguard.test", Phone = "9111111111", PasswordHash = "x" };
            db.AddRange(driver, route, bus, co,
                new StudentBusAssignment { StudentId = child.StudentId, Bus = bus, Route = route },
                new StudentGuardian { StudentId = child.StudentId, Guardian = co, Relationship = "Aunt", IsAuthorizedForPickup = false });
            await db.SaveChangesAsync();
            coGuardianId = co.GuardianId;
        }

        var client = _api.GuardianClient(child.GuardianId);
        var response = await client.GetAsync($"/api/me/students/{child.StudentId}");
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = System.Text.Json.JsonSerializer.Deserialize<GuardianStudentDetailDto>(raw, Json.Options)!;

        Assert.Equal("North loop", detail.Transport!.RouteDescription);
        Assert.Equal("Driver Dan", detail.Transport.DriverName);
        Assert.Equal(40, detail.Transport.Capacity);
        Assert.True(detail.HasRfidCard);
        Assert.Equal(2, detail.Guardians.Count);
        Assert.True(detail.Guardians[0].IsYou);
        Assert.Contains(detail.Guardians, g => g.GuardianId == coGuardianId && g.Relationship == "Aunt" && !g.IsAuthorizedForPickup);
        Assert.DoesNotContain("9111111111", raw);
        Assert.DoesNotContain("co.", raw.Split("\"guardians\"")[1].Split(']')[0].Replace("Co Guardian", ""));
    }

    [Fact]
    public async Task StudentEndpoints_ForAnotherGuardiansChild_AreForbidden()
    {
        var mine = await _api.NewChildAsync();
        var other = await _api.NewChildAsync();
        var client = _api.GuardianClient(mine.GuardianId);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/students/{other.StudentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/students/{other.StudentId}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/pickups?studentId={other.StudentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/me/students/{other.StudentId}/journey?from=2026-11-01")).StatusCode);
    }

    [Fact]
    public async Task Report_SummarisesAttendanceForTheRange()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var today = _api.Clock.Today;

        var report = await GetOkAsync<GuardianStudentReportDto>(_api.GuardianClient(child.GuardianId),
            $"/api/me/students/{child.StudentId}/report?from={today.AddDays(-6):yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(1, report.DaysPresent);
        var day = Assert.Single(report.Days);
        Assert.Equal(today, day.Date);
        Assert.NotNull(day.CheckIn);
        Assert.True(report.SchoolDaysObserved >= 1);
    }

    [Fact]
    public async Task Report_RejectsReversedOrTooLongRange()
    {
        var child = await _api.NewChildAsync();
        var client = _api.GuardianClient(child.GuardianId);

        var reversed = await client.GetAsync($"/api/me/students/{child.StudentId}/report?from=2026-11-10&to=2026-11-01");
        var tooLong = await client.GetAsync($"/api/me/students/{child.StudentId}/report?from=2026-01-01&to=2026-11-01");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task JourneyAndAttendance_WithoutRange_KeepOriginalBehaviour_AndRangeFilters()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var client = _api.GuardianClient(child.GuardianId);
        var today = _api.Clock.Today;

        var all = await GetOkAsync<List<JourneyEventDto>>(client, $"/api/me/students/{child.StudentId}/journey");
        var inRange = await GetOkAsync<List<JourneyEventDto>>(client, $"/api/me/students/{child.StudentId}/journey?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        var before = await GetOkAsync<List<JourneyEventDto>>(client, $"/api/me/students/{child.StudentId}/journey?from={today.AddDays(-5):yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}");
        var attendance = await GetOkAsync<List<AttendanceRecordDto>>(client, $"/api/me/students/{child.StudentId}/attendance?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Single(all);
        Assert.Single(inRange);
        Assert.Empty(before);
        Assert.Single(attendance);
    }

    [Fact]
    public async Task Pickups_ShowStateForOwnChildren()
    {
        var admin = await _api.AdminClientAsync();
        var child = await _api.NewChildAsync();
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        var verificationId = await admin.VerifyFaceAsync(_api, child);
        var client = _api.GuardianClient(child.GuardianId);

        var pending = await GetOkAsync<List<GuardianPickupDto>>(client, "/api/me/pickups");
        var dash = await GetOkAsync<GuardianDashboardDto>(client, "/api/me/dashboard");
        (await admin.ApproveAsync(verificationId)).EnsureSuccessStatusCode();
        var approved = await GetOkAsync<List<GuardianPickupDto>>(client, $"/api/me/pickups?studentId={child.StudentId}");

        var p = Assert.Single(pending);
        Assert.Equal(PickupVerificationState.Pending, p.State);
        Assert.Equal("Guardian", p.Kind);
        Assert.True(p.IsYou);
        Assert.Equal(1, dash.PendingPickups);
        Assert.Equal(PickupVerificationState.Approved, Assert.Single(approved).State);
    }
}
