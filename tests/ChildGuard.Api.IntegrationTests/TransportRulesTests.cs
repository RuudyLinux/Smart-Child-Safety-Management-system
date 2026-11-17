using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Transport rules against the real database: capacity, one active assignment per student
/// (in the service and as a database key), concurrent assignments, atomic bus changes, and
/// journey events that keep the bus and route they were recorded with.
/// </summary>
public class TransportRulesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public TransportRulesTests(ApiFactory api) => _api = api;

    private static string Uid(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private async Task<(HttpClient Admin, RouteDto Route, BusDto Bus)> BusAsync(int capacity, int? driverId = null)
    {
        var admin = await _api.AdminClientAsync();
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Uid("Route"), null), Json.Options));
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Uid("BUS"), capacity, driverId, route.RouteId), Json.Options));
        return (admin, route, bus);
    }

    private async Task<int> StudentAsync()
    {
        await using var db = _api.NewDb();
        var s = new Student { FullName = Uid("Kid"), DateOfBirth = new DateTime(2016, 2, 2) };
        db.Students.Add(s);
        await db.SaveChangesAsync();
        return s.StudentId;
    }

    private static Task<HttpResponseMessage> Assign(HttpClient admin, int studentId, int busId, int? routeId = null) =>
        admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(studentId, busId, routeId), Json.Options);

    [Fact]
    public async Task FullBus_IsRefused_AndOccupancyIsReported()
    {
        var (admin, _, bus) = await BusAsync(capacity: 1);
        Assert.Equal(HttpStatusCode.OK, (await Assign(admin, await StudentAsync(), bus.BusId)).StatusCode);

        var full = await Assign(admin, await StudentAsync(), bus.BusId);
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Contains("full", await full.MessageAsync());

        var listed = (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId);
        Assert.Equal(1, listed.ActiveStudentCount);

        var shrink = await admin.PutAsJsonAsync($"/api/transportation/buses/{bus.BusId}", new UpdateBusRequest(bus.BusNumber, 1, null, bus.RouteId), Json.Options);
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode); // equal to the occupancy is fine
    }

    [Fact]
    public async Task ConcurrentAssignments_NeverOverfillABus()
    {
        var (admin, _, bus) = await BusAsync(capacity: 2);
        var students = new List<int>();
        for (var i = 0; i < 6; i++) students.Add(await StudentAsync());

        var clients = new List<HttpClient>();
        for (var i = 0; i < 6; i++) clients.Add(await _api.AdminClientAsync());
        var results = await TestData.Simultaneously(6, i => Assign(clients[i], students[i], bus.BusId));

        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        await using var db = _api.NewDb();
        Assert.Equal(2, await db.StudentBusAssignments.CountAsync(a => a.BusId == bus.BusId && a.Status == RecordStatus.Active));
    }

    [Fact]
    public async Task ConcurrentAssignmentsOfOneStudent_LeaveExactlyOneActive()
    {
        var (admin, _, busA) = await BusAsync(capacity: 10);
        var (_, _, busB) = await BusAsync(capacity: 10);
        var (_, _, busC) = await BusAsync(capacity: 10);
        var student = await StudentAsync();
        var buses = new[] { busA.BusId, busB.BusId, busC.BusId, busA.BusId, busB.BusId, busC.BusId };
        var clients = new List<HttpClient>();
        for (var i = 0; i < buses.Length; i++) clients.Add(await _api.AdminClientAsync());

        await TestData.Simultaneously(buses.Length, i => Assign(clients[i], student, buses[i]));

        await using var db = _api.NewDb();
        var rows = await db.StudentBusAssignments.Where(a => a.StudentId == student).ToListAsync();
        Assert.Single(rows, a => a.Status == RecordStatus.Active);
        Assert.All(rows.Where(a => a.Status == RecordStatus.Inactive), a => Assert.NotNull(a.EndedAt));
    }

    [Fact]
    public async Task Database_RefusesASecondActiveAssignmentEvenOutsideTheApi()
    {
        var (_, _, bus) = await BusAsync(capacity: 10);
        var student = await StudentAsync();
        await using var db = _api.NewDb();
        db.StudentBusAssignments.Add(new StudentBusAssignment { StudentId = student, BusId = bus.BusId, Status = RecordStatus.Active });
        await db.SaveChangesAsync();

        db.StudentBusAssignments.Add(new StudentBusAssignment { StudentId = student, BusId = bus.BusId, Status = RecordStatus.Active });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Any number of ended rows is fine: that is the history.
        await using var db2 = _api.NewDb();
        db2.StudentBusAssignments.Add(new StudentBusAssignment { StudentId = student, BusId = bus.BusId, Status = RecordStatus.Inactive });
        db2.StudentBusAssignments.Add(new StudentBusAssignment { StudentId = student, BusId = bus.BusId, Status = RecordStatus.Inactive });
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task ChangingAndEndingAssignments_KeepsHistoryWithTimestamps()
    {
        var (admin, routeA, busA) = await BusAsync(capacity: 10);
        var (_, routeB, busB) = await BusAsync(capacity: 10);
        var student = await StudentAsync();

        await Ok<object>(await Assign(admin, student, busA.BusId));
        await Ok<object>(await Assign(admin, student, busB.BusId));

        var rows = (await Ok<List<StudentBusAssignmentDto>>(await admin.GetAsync("/api/transportation/assignments")))
            .Where(a => a.StudentId == student).ToList();
        var old = rows.Single(a => a.BusId == busA.BusId);
        var current = rows.Single(a => a.BusId == busB.BusId);
        Assert.Equal((RecordStatus.Inactive, routeA.RouteId), (old.Status, old.RouteId!.Value));
        Assert.NotNull(old.EndedAt);
        Assert.Equal((RecordStatus.Active, routeB.RouteId, (DateTime?)null), (current.Status, current.RouteId!.Value, current.EndedAt));

        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/assignments/{current.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));
        var reactivate = await admin.PatchAsJsonAsync($"/api/transportation/assignments/{current.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Active), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, reactivate.StatusCode);

        await using var db = _api.NewDb();
        Assert.Equal(2, await db.StudentBusAssignments.CountAsync(a => a.StudentId == student && a.Status == RecordStatus.Inactive && a.EndedAt != null));
    }

    [Fact]
    public async Task OutOfServiceBusDeactivatedDriverAndRouteOverride_AreRefused()
    {
        var admin = await _api.AdminClientAsync();
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Uid("Driver"), null, Uid("LIC")), Json.Options));
        var (_, route, bus) = await BusAsync(capacity: 10, driverId: driver.DriverId);
        var seated = await StudentAsync();
        await Ok<object>(await Assign(admin, seated, bus.BusId));

        // Deactivations keep every existing relationship.
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/drivers/{driver.DriverId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));
        var after = (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId);
        Assert.Equal((driver.DriverId, 1), (after.DriverId!.Value, after.ActiveStudentCount));

        Assert.Equal(HttpStatusCode.BadRequest, (await Assign(admin, await StudentAsync(), bus.BusId)).StatusCode);
        var newBus = await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Uid("BUS"), 10, driver.DriverId, null), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, newBus.StatusCode);

        var (_, _, other) = await BusAsync(capacity: 10);
        var overridden = await Assign(admin, await StudentAsync(), other.BusId, route.RouteId);
        Assert.Equal(HttpStatusCode.BadRequest, overridden.StatusCode); // Transport:AllowRouteOverride defaults to false
    }

    [Fact]
    public async Task DuplicateRouteNames_AreRefusedByTheDatabaseToo()
    {
        var admin = await _api.AdminClientAsync();
        var name = Uid("Route");
        await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(name, null), Json.Options));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(name.ToLowerInvariant(), null), Json.Options)).StatusCode);

        await using var db = _api.NewDb();
        db.Routes.Add(new Route { RouteName = name.ToLowerInvariant() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task JourneyEvents_KeepTheBusAndRouteTheyWereRecordedWith()
    {
        var child = await _api.NewChildAsync(withFace: false);
        var (admin, routeA, busA) = await BusAsync(capacity: 10);
        await Ok<object>(await Assign(admin, child.StudentId, busA.BusId));
        var assignmentId = (await Ok<List<StudentBusAssignmentDto>>(await admin.GetAsync("/api/transportation/assignments")))
            .Single(a => a.StudentId == child.StudentId && a.Status == RecordStatus.Active).Id;

        await admin.ScanOkAsync(child, JourneyEventType.BusBoarding);

        // Change everything about the bus, then move the student to another bus.
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Uid("Driver"), null, null), Json.Options));
        var routeC = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Uid("Route"), null), Json.Options));
        await Ok<object>(await admin.PutAsJsonAsync($"/api/transportation/buses/{busA.BusId}", new UpdateBusRequest(busA.BusNumber, 10, driver.DriverId, routeC.RouteId), Json.Options));
        var (_, routeB, busB) = await BusAsync(capacity: 10);
        await Ok<object>(await Assign(admin, child.StudentId, busB.BusId));
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/buses/{busA.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        await using (var db = _api.NewDb())
        {
            var boarding = await db.JourneyEvents.SingleAsync(e => e.StudentId == child.StudentId && e.EventType == JourneyEventType.BusBoarding);
            Assert.Equal((busA.BusId, routeA.RouteId, assignmentId), (boarding.BusId!.Value, boarding.RouteId!.Value, boarding.StudentBusAssignmentId!.Value));
        }

        // Still visible through the API with the original route, after the assignment ended.
        var history = await Ok<List<JourneyEventDto>>(await admin.GetAsync($"/api/journey/{child.StudentId}"));
        var e = history.Single(x => x.EventType == JourneyEventType.BusBoarding);
        Assert.Equal((busA.BusNumber, routeA.RouteName), (e.BusNumber, e.RouteName));

        // A new event records the new bus.
        await admin.ScanOkAsync(child, JourneyEventType.SchoolArrival);
        await using (var db = _api.NewDb())
        {
            var arrival = await db.JourneyEvents.SingleAsync(x => x.StudentId == child.StudentId && x.EventType == JourneyEventType.SchoolArrival);
            Assert.Equal((busB.BusId, routeB.RouteId), (arrival.BusId!.Value, arrival.RouteId!.Value));
        }
    }

    [Fact]
    public async Task AdminChanges_AreAuditedWithTheAdmin()
    {
        var (admin, _, bus) = await BusAsync(capacity: 5);
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        var audit = await Ok<List<TransportAuditDto>>(await admin.GetAsync("/api/transportation/audit?take=100"));
        var entries = audit.Where(a => a.EntityType == "Bus" && a.EntityId == bus.BusId).ToList();
        Assert.Contains(entries, a => a.Action == "Created");
        Assert.Contains(entries, a => a.Action == "TakenOutOfService");
        Assert.All(entries, a => Assert.False(string.IsNullOrEmpty(a.AdminName)));

        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/transportation/audit")).StatusCode);
    }

    [Fact]
    public async Task StudentImport_CreatesAssignmentsWithinCapacity()
    {
        var (admin, route, bus) = await BusAsync(capacity: 1);
        ImportRowDto Row(int n) => new(n, new() { ["fullName"] = Uid("Imp"), ["dateOfBirth"] = "2016-03-03", ["busNumber"] = bus.BusNumber });

        var summary = await Ok<ImportSummaryDto>(await admin.PostAsJsonAsync("/api/imports/commit",
            new CommitImportRequest(ImportCategory.Students, ImportSource.Manual, null, new List<ImportRowDto> { Row(1), Row(2) }), Json.Options));

        Assert.Equal((ImportStatus.CompletedWithErrors, 1, 1), (summary.Status, summary.InsertedRows, summary.FailedRows));
        Assert.Contains("full", summary.FailedRowDetails.Single().Errors.Single().Message);
        await using var db = _api.NewDb();
        var seated = await db.StudentBusAssignments.SingleAsync(a => a.BusId == bus.BusId && a.Status == RecordStatus.Active);
        Assert.Equal(route.RouteId, seated.RouteId);
    }
}
