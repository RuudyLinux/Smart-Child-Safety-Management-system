using System.Net;
using System.Net.Http.Json;
using ChildGuard.Api.IntegrationTests.Infrastructure;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>
/// Edit and deactivate for drivers, routes, buses and bus assignments, and add/rename
/// for checkpoint scanners. Nothing is hard-deleted: history keeps pointing at the
/// same rows, which change status instead.
/// </summary>
public class TransportAndScannerCrudTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;
    public TransportAndScannerCrudTests(ApiFactory api) => _api = api;

    private static string Uid(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<T> Ok<T>(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<T>(Json.Options))!;
    }

    private async Task<(HttpClient Admin, DriverDto Driver, RouteDto Route, BusDto Bus)> FleetAsync()
    {
        var admin = await _api.AdminClientAsync();
        var driver = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Uid("Driver"), "9876500001", "DL-1"), Json.Options));
        var route = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Uid("Route"), null), Json.Options));
        var bus = await Ok<BusDto>(await admin.PostAsJsonAsync("/api/transportation/buses", new CreateBusRequest(Uid("BUS"), 40, driver.DriverId, route.RouteId), Json.Options));
        return (admin, driver, route, bus);
    }

    [Fact]
    public async Task Driver_CanBeEditedAndDeactivated()
    {
        var (admin, driver, _, _) = await FleetAsync();
        var name = Uid("Renamed");

        await Ok<object>(await admin.PutAsJsonAsync($"/api/transportation/drivers/{driver.DriverId}", new UpdateDriverRequest(name, "9000000000", "DL-NEW"), Json.Options));
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/drivers/{driver.DriverId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        var saved = (await Ok<List<DriverDto>>(await admin.GetAsync("/api/transportation/drivers"))).Single(d => d.DriverId == driver.DriverId);
        Assert.Equal((name, "9000000000", "DL-NEW", RecordStatus.Inactive), (saved.FullName, saved.Phone, saved.LicenseNumber, saved.Status));
    }

    [Fact]
    public async Task Route_CanBeEditedButNotToADuplicateName_AndDeactivated()
    {
        var (admin, _, route, _) = await FleetAsync();
        var other = await Ok<RouteDto>(await admin.PostAsJsonAsync("/api/transportation/routes", new CreateRouteRequest(Uid("Other"), null), Json.Options));

        var duplicate = await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}", new UpdateRouteRequest(other.RouteName.ToLowerInvariant(), null), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var name = Uid("Loop");
        await Ok<object>(await admin.PutAsJsonAsync($"/api/transportation/routes/{route.RouteId}", new UpdateRouteRequest(name, "Via the market"), Json.Options));
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/routes/{route.RouteId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        var saved = (await Ok<List<RouteDto>>(await admin.GetAsync("/api/transportation/routes"))).Single(r => r.RouteId == route.RouteId);
        Assert.Equal((name, "Via the market", RecordStatus.Inactive), (saved.RouteName, saved.Description, saved.Status));
    }

    [Fact]
    public async Task Bus_CanBeEdited_ExposesItsDriverAndRouteIds_AndGoesOutOfService()
    {
        var (admin, _, _, bus) = await FleetAsync();
        var driver2 = await Ok<DriverDto>(await admin.PostAsJsonAsync("/api/transportation/drivers", new CreateDriverRequest(Uid("Driver"), null, null), Json.Options));

        var number = Uid("BUS");
        await Ok<object>(await admin.PutAsJsonAsync($"/api/transportation/buses/{bus.BusId}", new UpdateBusRequest(number, 52, driver2.DriverId, null), Json.Options));
        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/buses/{bus.BusId}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        var saved = (await Ok<List<BusDto>>(await admin.GetAsync("/api/transportation/buses"))).Single(b => b.BusId == bus.BusId);
        Assert.Equal((number, 52, driver2.FullName, (string?)null, RecordStatus.Inactive), (saved.BusNumber, saved.Capacity, saved.DriverName, saved.RouteName, saved.Status));
        Assert.Equal(driver2.DriverId, saved.DriverId);
        Assert.Null(saved.RouteId);
    }

    [Fact]
    public async Task Bus_CannotTakeAnotherBusesNumber()
    {
        var (admin, _, _, bus) = await FleetAsync();
        var (_, _, _, other) = await FleetAsync();

        var response = await admin.PutAsJsonAsync($"/api/transportation/buses/{bus.BusId}", new UpdateBusRequest(other.BusNumber, 40, null, null), Json.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Assignment_CanBeEnded_AndTheRowIsKeptAsHistory()
    {
        var (admin, _, _, bus) = await FleetAsync();
        var child = await _api.NewChildAsync();
        (await admin.PostAsJsonAsync("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(child.StudentId, bus.BusId, null), Json.Options)).EnsureSuccessStatusCode();
        var assignment = (await Ok<List<StudentBusAssignmentDto>>(await admin.GetAsync("/api/transportation/assignments"))).Single(a => a.StudentId == child.StudentId);

        await Ok<object>(await admin.PatchAsJsonAsync($"/api/transportation/assignments/{assignment.Id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options));

        await using var db = _api.NewDb();
        var row = await db.StudentBusAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(RecordStatus.Inactive, row.Status);
    }

    [Fact]
    public async Task Edits_OfMissingRecords_Return404()
    {
        var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/transportation/drivers/999999", new UpdateDriverRequest("X", null, null), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync("/api/transportation/buses/999999/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/rfid/devices/999999", new UpdateRfidDeviceRequest("X"), Json.Options)).StatusCode);
    }

    [Fact]
    public async Task Scanner_CanBeAddedAndRenamed_ButLabelsStayUnique()
    {
        var admin = await _api.AdminClientAsync();
        var label = Uid("GATE");

        var created = await Ok<RfidDeviceDto>(await admin.PostAsJsonAsync("/api/rfid/devices", new CreateRfidDeviceRequest(label), Json.Options));
        var duplicate = await admin.PostAsJsonAsync("/api/rfid/devices", new CreateRfidDeviceRequest(label.ToLowerInvariant()), Json.Options);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var renamed = Uid("NORTH-GATE");
        await Ok<object>(await admin.PutAsJsonAsync($"/api/rfid/devices/{created.RfidDeviceId}", new UpdateRfidDeviceRequest(renamed), Json.Options));
        var devices = await Ok<List<RfidDeviceDto>>(await admin.GetAsync("/api/rfid/devices"));
        Assert.Contains(devices, d => d.RfidDeviceId == created.RfidDeviceId && d.DeviceLabel == renamed);

        // A new scanner works at once: a card read through it records the checkpoint.
        var child = await _api.NewChildAsync();
        var scan = await Ok<RfidScanResultDto>(await admin.PostAsJsonAsync("/api/rfid/scan",
            new RfidScanRequest(child.CardUid, created.RfidDeviceId, JourneyEventType.SchoolArrival), Json.Options));
        Assert.True(scan.EventRecorded);
    }

    [Fact]
    public async Task TransportAndScannerEdits_AreAdminOnly()
    {
        var child = await _api.NewChildAsync();
        var guardian = _api.GuardianClient(child.GuardianId);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PostAsJsonAsync("/api/rfid/devices", new CreateRfidDeviceRequest("X"), Json.Options)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.PutAsJsonAsync("/api/transportation/drivers/1", new UpdateDriverRequest("X", null, null), Json.Options)).StatusCode);
    }
}
