using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Milestone 3D against the real database: ordered route stops, pickup and drop-off stops on assignments and at
/// enrollment, stop removal versus deactivation, route deletion only without history, and the student's transport
/// summary. Existing capacity and route rules keep applying.
/// </summary>
public class RouteStopsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public RouteStopsTests(ApiFactory api) => _api = api;

    private static string Unique(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private static Task<HttpResponseMessage> Delete(HttpClient c, string url) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(new DeleteRecordRequest("Added by mistake during setup."), options: Json.Options) });

    private async Task<int> StudentAsync()
    {
        await using var db = _api.NewDb();
        var s = new Student { FullName = Unique("Stop Kid "), DateOfBirth = new DateTime(2016, 4, 4), Grade = "3" };
        db.Students.Add(s);
        await db.SaveChangesAsync();
        return s.StudentId;
    }

    private static async Task<RouteDto> RouteAsync(HttpClient admin) =>
        await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Unique("Stops R "), null), Json.Options));

    private static async Task<RouteStopDto> StopAsync(HttpClient admin, int routeId, string name, TimeOnly? pickup = null) =>
        await Ok<RouteStopDto>(await admin.PostAsJsonAsync($"/api/transportation/routes/{routeId}/stops",
            new RouteStopRequest(name, $"{name} junction", pickup, pickup?.AddHours(8)), Json.Options));

    private static async Task<RouteDetailDto> DetailAsync(HttpClient admin, int routeId) =>
        await Ok<RouteDetailDto>(await admin.GetAsync($"/api/transportation/routes/{routeId}"));

    private static async Task<StudentBusAssignmentDto> ActiveAssignmentAsync(HttpClient admin, int studentId) =>
        (await Ok<List<StudentBusAssignmentDto>>(await admin.GetAsync("/api/transportation/assignments")))
            .Single(a => a.StudentId == studentId && a.Status == RecordStatus.Active);

    [Fact]
    public async Task Stops_AreAppendedInOrder_Reordered_AndRouteEndsAreSaved()
    {
        var admin = await _api.AdminClientAsync();
        var route = await RouteAsync(admin);
        var a = await StopAsync(admin, route.RouteId, "Alpha", new TimeOnly(7, 10));
        var b = await StopAsync(admin, route.RouteId, "Bravo", new TimeOnly(7, 20));
        var c = await StopAsync(admin, route.RouteId, "Charlie");
        Assert.Equal(new[] { 1, 2, 3 }, new[] { a.Sequence, b.Sequence, c.Sequence });

        (await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/order", new ReorderStopsRequest([c.RouteStopId, a.RouteStopId, b.RouteStopId]), Json.Options)).EnsureSuccessStatusCode();
        var detail = await DetailAsync(admin, route.RouteId);
        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, detail.Stops.OrderBy(s => s.Sequence).Select(s => s.Name));
        Assert.Equal(new TimeOnly(7, 10), detail.Stops.Single(s => s.Name == "Alpha").PickupTime);
        Assert.Equal(3, detail.Route.StopCount);

        // The order must name every active stop exactly once.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/order", new ReorderStopsRequest([a.RouteStopId, b.RouteStopId]), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/order", new ReorderStopsRequest([a.RouteStopId, a.RouteStopId, b.RouteStopId]), Json.Options)).StatusCode);

        (await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/{a.RouteStopId}", new RouteStopRequest("Alpha Gate", null, new TimeOnly(7, 15), null), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}/details", new UpdateRouteDetailsRequest("Depot", "School gate"), Json.Options)).EnsureSuccessStatusCode();
        detail = await DetailAsync(admin, route.RouteId);
        Assert.Equal(("Depot", "School gate"), (detail.Route.StartPoint, detail.Route.Destination));
        var edited = detail.Stops.Single(s => s.RouteStopId == a.RouteStopId);
        Assert.Equal(("Alpha Gate", (string?)null, new TimeOnly(7, 15)), (edited.Name, edited.Address, edited.PickupTime!.Value));

        // A stop of another route cannot be edited through this route.
        var other = await RouteAsync(admin);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/transportation/routes/{other.RouteId}/stops/{a.RouteStopId}", new RouteStopRequest("X", null, null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops", new RouteStopRequest("", null, null, null), Json.Options)).StatusCode);

        await using var db = _api.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "Route" && e.EntityId == route.RouteId && e.Action == "RouteStopsReordered"));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "RouteStop" && e.EntityId == a.RouteStopId && e.Action == "RouteStopUpdated"));
    }

    [Fact]
    public async Task AssignmentStops_MustBeActiveStopsOfTheStudentsRoute()
    {
        var admin = await _api.AdminClientAsync();
        var route = await RouteAsync(admin);
        var other = await RouteAsync(admin);
        var p = await StopAsync(admin, route.RouteId, "Pickup Point");
        var d = await StopAsync(admin, route.RouteId, "Drop Point");
        var foreign = await StopAsync(admin, other.RouteId, "Elsewhere");
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("SB"), 1, null, route.RouteId), Json.Options));
        var noRouteBus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("SB"), 5, null, null), Json.Options));
        var s1 = await StudentAsync();

        var wrong = await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s1, bus.BusId, null, foreign.RouteStopId, null), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("pickup", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/transportation/assignments",
            new CreateStudentBusAssignmentRequest(s1, noRouteBus.BusId, null, p.RouteStopId, null), Json.Options)).StatusCode);

        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s1, bus.BusId, null, p.RouteStopId, d.RouteStopId), Json.Options)).EnsureSuccessStatusCode();
        var a = await ActiveAssignmentAsync(admin, s1);
        Assert.Equal(("Pickup Point", "Drop Point"), (a.PickupStop, a.DropStop));

        // Capacity rules are unchanged: the bus has one seat.
        var s2 = await StudentAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s2, bus.BusId, null, p.RouteStopId, null), Json.Options)).StatusCode);

        // Swap stops on the current assignment; a stop from another route is refused.
        (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/stops", new UpdateAssignmentStopsRequest(d.RouteStopId, p.RouteStopId), Json.Options)).EnsureSuccessStatusCode();
        Assert.Equal(("Drop Point", "Pickup Point"), ((await ActiveAssignmentAsync(admin, s1)).PickupStop, (await ActiveAssignmentAsync(admin, s1)).DropStop));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/stops", new UpdateAssignmentStopsRequest(foreign.RouteStopId, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/stops", new UpdateAssignmentStopsRequest(0, null), Json.Options)).StatusCode);

        // The roster and the student's profile show the stops with their times.
        var roster = (await DetailAsync(admin, route.RouteId)).Students.Single(r => r.StudentId == s1);
        Assert.Equal(("Drop Point", "Pickup Point"), (roster.PickupStop, roster.DropStop));
        var student = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{s1}"));
        Assert.NotNull(student.Transport);
        Assert.Equal((bus.BusNumber, route.RouteName, "Drop Point"), (student.Transport!.BusNumber, student.Transport.RouteName, student.Transport.PickupStop));
        Assert.Single(student.TransportHistory!);

        // Inactive stops cannot be chosen.
        var spare = await StopAsync(admin, route.RouteId, "Spare");
        (await admin.PatchAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/{spare.RouteStopId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        var inactive = await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/stops", new UpdateAssignmentStopsRequest(spare.RouteStopId, null), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("deactivated", await inactive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RemovingAStop_IsRefusedWhileUsed_DeactivatesWhenUsedBefore_AndDeletesOtherwise()
    {
        var admin = await _api.AdminClientAsync();
        var route = await RouteAsync(admin);
        var used = await StopAsync(admin, route.RouteId, "Used");
        var unused = await StopAsync(admin, route.RouteId, "Unused");
        var last = await StopAsync(admin, route.RouteId, "Last");
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("RB"), 10, null, route.RouteId), Json.Options));
        var s = await StudentAsync();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s, bus.BusId, null, used.RouteStopId, null), Json.Options)).EnsureSuccessStatusCode();

        var refused = await admin.DeleteAsync($"/api/transportation/routes/{route.RouteId}/stops/{used.RouteStopId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/{used.RouteStopId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).StatusCode);

        (await admin.DeleteAsync($"/api/transportation/routes/{route.RouteId}/stops/{unused.RouteStopId}")).EnsureSuccessStatusCode();
        var detail = await DetailAsync(admin, route.RouteId);
        Assert.DoesNotContain(detail.Stops, x => x.RouteStopId == unused.RouteStopId);
        Assert.Equal(new[] { ("Used", 1), ("Last", 2) }, detail.Stops.OrderBy(x => x.Sequence).Select(x => (x.Name, x.Sequence)));

        // Once the student has left the stop, it is kept as history rather than deleted.
        var a = await ActiveAssignmentAsync(admin, s);
        (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        var removed = await Ok<MessageBody>(await admin.DeleteAsync($"/api/transportation/routes/{route.RouteId}/stops/{used.RouteStopId}"));
        Assert.Contains("deactivated", removed.Message);

        detail = await DetailAsync(admin, route.RouteId);
        Assert.Equal(RecordStatus.Inactive, detail.Stops.Single(x => x.RouteStopId == used.RouteStopId).Status);
        Assert.Equal(1, detail.Stops.Single(x => x.RouteStopId == last.RouteStopId).Sequence);
        Assert.Equal(1, detail.Route.StopCount);

        // History still names the stop, and it can be brought back into use.
        var student = await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{s}"));
        Assert.Equal("Used", Assert.Single(student.TransportHistory!).PickupStop);
        (await admin.PatchAsJsonAsync($"/api/transportation/routes/{route.RouteId}/stops/{used.RouteStopId}/status", new UpdateTransportStatusRequest(RecordStatus.Active), Json.Options)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await DetailAsync(admin, route.RouteId)).Route.StopCount);
    }

    [Fact]
    public async Task DeletingARoute_IsBlockedByBusesAndHistory_AndRemovesItsStopsOtherwise()
    {
        var admin = await _api.AdminClientAsync();
        var route = await RouteAsync(admin);
        var stop = await StopAsync(admin, route.RouteId, "Only");
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("DB"), 10, null, route.RouteId), Json.Options));

        var blocked = await DetailAsync(admin, route.RouteId);
        Assert.False(blocked.Deletion.CanDelete);
        Assert.Contains(blocked.Deletion.Blockers, b => b.Contains("bus"));
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, $"/api/transportation/routes/{route.RouteId}")).StatusCode);

        (await admin.PutAsJsonAsync($"/api/transportation/buses/{bus.BusId}", new UpdateBusRequest(bus.BusNumber, 10, null, null), Json.Options)).EnsureSuccessStatusCode();
        var free = await DetailAsync(admin, route.RouteId);
        Assert.True(free.Deletion.CanDelete, string.Join("; ", free.Deletion.Blockers));
        Assert.Contains(free.Deletion.AlsoRemoved, x => x.Contains("1 stop"));
        (await Delete(admin, $"/api/transportation/routes/{route.RouteId}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/transportation/routes/{route.RouteId}")).StatusCode);
        await using var db = _api.NewDb();
        Assert.False(await db.RouteStops.AnyAsync(s => s.RouteStopId == stop.RouteStopId));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "Route" && e.EntityId == route.RouteId && e.Action == "RouteDeleted"));

        // A route with past assignments stays.
        var kept = await RouteAsync(admin);
        var keptBus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("DB"), 10, null, kept.RouteId), Json.Options));
        var s = await StudentAsync();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s, keptBus.BusId, null), Json.Options)).EnsureSuccessStatusCode();
        var a = await ActiveAssignmentAsync(admin, s);
        (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/transportation/buses/{keptBus.BusId}", new UpdateBusRequest(keptBus.BusNumber, 10, null, null), Json.Options)).EnsureSuccessStatusCode();
        var history = await DetailAsync(admin, kept.RouteId);
        Assert.False(history.Deletion.CanDelete);
        Assert.Contains(history.Deletion.Blockers, b => b.Contains("assignment"));
    }

    [Fact]
    public async Task Enrollment_SavesPickupAndDropStops_AndRejectsAStopOffTheRoute()
    {
        var admin = await _api.AdminClientAsync();
        var route = await RouteAsync(admin);
        var p = await StopAsync(admin, route.RouteId, "Home Lane", new TimeOnly(7, 5));
        var other = await RouteAsync(admin);
        var foreign = await StopAsync(admin, other.RouteId, "Far Away");
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("EB"), 10, null, route.RouteId), Json.Options));

        EnrollStudentRequest Request(int? pickup) => new(Guid.NewGuid().ToString("N"),
            new EnrollmentStudentDto(Unique("Enrolled Rider "), new DateTime(2017, 2, 2), "2", null, null, null, null),
            [new EnrollmentGuardianDto(null, Unique("Parent "), $"{Unique("p")}@stops.test", "98" + Random.Shared.Next(10000000, 99999999), null, false, "Mother", true)],
            null, new EnrollmentTransportDto(bus.BusId, null, pickup, pickup));

        var bad = await admin.PostAsJsonAsync("/api/enrollments", Request(foreign.RouteStopId), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await Ok<EnrollmentResultDto>(await admin.PostAsJsonAsync("/api/enrollments", Request(p.RouteStopId), Json.Options));
        var t = (await Ok<StudentDetailDto>(await admin.GetAsync($"/api/students/{ok.StudentId}"))).Transport!;
        Assert.Equal(("Home Lane", new TimeOnly(7, 5), "Home Lane"), (t.PickupStop, t.PickupTime!.Value, t.DropStop));
    }

    [Fact]
    public async Task StopEndpoints_RequireAnAdmin()
    {
        var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/transportation/routes/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/transportation/routes/1/stops", new RouteStopRequest("X", null, null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsJsonAsync("/api/transportation/assignments/1/stops", new UpdateAssignmentStopsRequest(null, null), Json.Options)).StatusCode);

        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/transportation/routes/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PutAsJsonAsync("/api/transportation/routes/1/stops/order", new ReorderStopsRequest([]), Json.Options)).StatusCode);
    }

    private sealed record MessageBody(string Message);
}
