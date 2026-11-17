using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Milestone 3C against the real database: driver profiles and licence expiry, bus vehicle details and maintenance
/// states, live rosters computed from assignments, and deletion only when nothing depends on the record.
/// The test clock puts "today" at the school on 6 Nov 2026.
/// </summary>
public class FleetTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public FleetTests(ApiFactory api) => _api = api;

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
        var s = new Student { FullName = Unique("Fleet Kid "), DateOfBirth = new DateTime(2016, 4, 4), Grade = "3" };
        db.Students.Add(s);
        await db.SaveChangesAsync();
        return s.StudentId;
    }

    private static UpdateDriverProfileRequest Profile(string name, DateOnly? expires, string? code = null) =>
        new(name, "9800012345", "DL-123", code, "driver@fleet.test",
            new AddressDto("5 Depot Road", null, "Peenya", null, "Bengaluru", null, "Karnataka", "560058", "India"),
            "Sita", "9800099999", "HTV", expires, new DateOnly(2020, 6, 1));

    [Fact]
    public async Task DriverProfile_IsSaved_LicenceExpiryIsClassified_AndDriverIdsAreUnique()
    {
        var admin = await _api.AdminClientAsync();
        var a = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Unique("Ravi "), null, null), Json.Options));
        var b = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Unique("Anil "), null, null), Json.Options));
        var code = Unique("DRV-");

        (await admin.PutAsJsonAsync($"/api/transportation/drivers/{a.DriverId}/profile", Profile(a.FullName, new DateOnly(2026, 11, 20), code), Json.Options)).EnsureSuccessStatusCode();
        var detail = await Ok<DriverDetailDto>(await admin.GetAsync($"/api/transportation/drivers/{a.DriverId}"));
        Assert.Equal((code, "HTV", LicenceState.ExpiringSoon), (detail.Driver.EmployeeCode, detail.Driver.LicenseCategory, detail.Driver.LicenceState));
        Assert.Equal("Peenya", detail.Address!.Locality);
        Assert.Equal(("Sita", new DateOnly(2020, 6, 1)), (detail.EmergencyContactName, detail.JoinedOn!.Value));

        var taken = await admin.PutAsJsonAsync($"/api/transportation/drivers/{b.DriverId}/profile", Profile(b.FullName, null, code.ToLowerInvariant()), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);

        (await admin.PutAsJsonAsync($"/api/transportation/drivers/{b.DriverId}/profile", Profile(b.FullName, new DateOnly(2026, 11, 1)), Json.Options)).EnsureSuccessStatusCode();
        var list = await Ok<List<DriverDto>>(await admin.GetAsync("/api/transportation/drivers"));
        Assert.Equal(LicenceState.Expired, list.Single(d => d.DriverId == b.DriverId).LicenceState);
        (await admin.PutAsJsonAsync($"/api/transportation/drivers/{b.DriverId}/profile", Profile(b.FullName, new DateOnly(2028, 1, 1)), Json.Options)).EnsureSuccessStatusCode();
        Assert.Equal(LicenceState.Valid, (await Ok<List<DriverDto>>(await admin.GetAsync("/api/transportation/drivers"))).Single(d => d.DriverId == b.DriverId).LicenceState);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/transportation/drivers/{a.DriverId}/profile",
            Profile(a.FullName, null) with { Email = "not-an-email" }, Json.Options)).StatusCode);

        await using var db = _api.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "Driver" && e.EntityId == a.DriverId && e.Action == "DriverProfileUpdated"));
    }

    [Fact]
    public async Task Rosters_AreComputedLive_ForTheBusAndItsDriver()
    {
        var admin = await _api.AdminClientAsync();
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Unique("Mohan "), "9811", null), Json.Options));
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Unique("R "), "East loop"), Json.Options));
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("FB"), 30, driver.DriverId, route.RouteId), Json.Options));
        var s1 = await StudentAsync();
        var s2 = await StudentAsync();
        foreach (var s in new[] { s1, s2 })
            (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s, bus.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        var detail = await Ok<BusDetailDto>(await admin.GetAsync($"/api/transportation/buses/{bus.BusId}"));
        Assert.Equal(2, detail.Bus.ActiveStudentCount);
        Assert.Equal(new[] { s1, s2 }.OrderBy(x => x), detail.Students.Select(r => r.StudentId).OrderBy(x => x));
        Assert.Equal(driver.DriverId, detail.Driver!.DriverId);
        Assert.Equal(route.RouteName, detail.Route!.RouteName);

        var driverDetail = await Ok<DriverDetailDto>(await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}"));
        Assert.Equal(bus.BusNumber, Assert.Single(driverDetail.Buses).BusNumber);
        Assert.Equal(2, driverDetail.Students.Count);
        Assert.Equal(2, driverDetail.Driver.StudentCount);

        var a1 = (await Ok<List<StudentBusAssignmentDto>>(await admin.GetAsync("/api/transportation/assignments"))).Single(a => a.StudentId == s1 && a.Status == RecordStatus.Active);
        (await admin.PatchAsJsonAsync($"/api/transportation/assignments/{a1.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        detail = await Ok<BusDetailDto>(await admin.GetAsync($"/api/transportation/buses/{bus.BusId}"));
        Assert.Equal(1, detail.Bus.ActiveStudentCount);
        Assert.Equal(s2, Assert.Single(detail.Students).StudentId);
        Assert.Equal(1, (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId).ActiveStudentCount);
    }

    [Fact]
    public async Task VehicleDetails_AreSaved_AndRegistrationNumbersAreUnique()
    {
        var admin = await _api.AdminClientAsync();
        var a = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("VB"), 40, null, null), Json.Options));
        var b = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("VB"), 40, null, null), Json.Options));
        var reg = "KA 01 " + Random.Shared.Next(1000, 9999);

        (await admin.PutAsJsonAsync($"/api/transportation/buses/{a.BusId}/details", new UpdateBusDetailsRequest(reg.ToLowerInvariant(), "Tata", "Starbus"), Json.Options)).EnsureSuccessStatusCode();
        var saved = (await Ok<BusDetailDto>(await admin.GetAsync($"/api/transportation/buses/{a.BusId}"))).Bus;
        Assert.Equal((reg, "Tata", "Starbus"), (saved.RegistrationNumber, saved.Manufacturer, saved.Model));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/transportation/buses/{b.BusId}/details", new UpdateBusDetailsRequest(reg, null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/transportation/buses/{b.BusId}/details", new UpdateBusDetailsRequest("KA<01>", null, null), Json.Options)).StatusCode);
    }

    [Fact]
    public async Task MaintenanceStatuses_BlockNewStudents_ButKeepCurrentOnes()
    {
        var admin = await _api.AdminClientAsync();
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("MB"), 30, null, null), Json.Options));
        var onBoard = await StudentAsync();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(onBoard, bus.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        foreach (var status in new[] { BusStatus.UnderMaintenance, BusStatus.OutOfService })
        {
            (await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/operational-status", new BusOperationalStatusRequest(status, "Brake service"), Json.Options)).EnsureSuccessStatusCode();
            var detail = await Ok<BusDetailDto>(await admin.GetAsync($"/api/transportation/buses/{bus.BusId}"));
            Assert.Equal((status, 1), (detail.Bus.Status, detail.Bus.ActiveStudentCount));
            Assert.Contains(detail.Deletion.Warnings, w => w.Contains("1 student"));
            var refused = await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(await StudentAsync(), bus.BusId, null), Json.Options);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("out of service", await refused.MessageAsync());
        }

        (await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/operational-status", new BusOperationalStatusRequest(BusStatus.Active, null), Json.Options)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(await StudentAsync(), bus.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        // The original status endpoint still sets Active / Inactive.
        (await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).EnsureSuccessStatusCode();
        Assert.Equal(BusStatus.Inactive, (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId).Status);

        await using var db = _api.NewDb();
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == "Bus" && e.EntityId == bus.BusId && e.Action == "BusSetUnderMaintenance"));
    }

    [Fact]
    public async Task DriversAndBuses_AreDeletedOnlyWithoutDependents()
    {
        var admin = await _api.AdminClientAsync();
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Unique("Del "), null, null), Json.Options));
        var used = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("DB"), 30, driver.DriverId, null), Json.Options));
        var fresh = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Unique("DB"), 30, null, null), Json.Options));
        var s = await StudentAsync();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(s, used.BusId, null), Json.Options)).EnsureSuccessStatusCode();

        var driverRefused = await Delete(admin, $"/api/transportation/drivers/{driver.DriverId}");
        Assert.Equal(HttpStatusCode.Conflict, driverRefused.StatusCode);
        Assert.Contains(used.BusNumber, await driverRefused.MessageAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, $"/api/transportation/buses/{used.BusId}")).StatusCode);

        (await admin.PutAsJsonAsync($"/api/transportation/buses/{used.BusId}", new UpdateBusRequest(used.BusNumber, 30, null, null), Json.Options)).EnsureSuccessStatusCode();
        await Ok<LifecycleResultDto>(await Delete(admin, $"/api/transportation/drivers/{driver.DriverId}"));
        await Ok<LifecycleResultDto>(await Delete(admin, $"/api/transportation/buses/{fresh.BusId}"));

        await using var db = _api.NewDb();
        Assert.False(await db.Drivers.AnyAsync(d => d.DriverId == driver.DriverId));
        Assert.False(await db.Buses.AnyAsync(b => b.BusId == fresh.BusId));
        Assert.True(await db.Buses.AnyAsync(b => b.BusId == used.BusId)); // has history
        Assert.True(await db.AuditEntries.AnyAsync(e => e.Action == "DriverDeleted" && e.EntityId == driver.DriverId));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/transportation/drivers/{driver.DriverId}")).StatusCode);
    }

    [Fact]
    public async Task FleetEndpoints_AreAdminOnly()
    {
        var guardian = _api.GuardianClient((await _api.NewChildAsync(withFace: false)).GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/transportation/drivers/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/transportation/buses/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PutAsJsonAsync("/api/transportation/drivers/1/profile", Profile("X", null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PutAsJsonAsync("/api/transportation/buses/1/details", new UpdateBusDetailsRequest("X", null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PatchAsJsonAsync("/api/transportation/buses/1/operational-status", new BusOperationalStatusRequest(BusStatus.Active, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Delete(guardian, "/api/transportation/buses/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/transportation/buses/1")).StatusCode);
    }
}
