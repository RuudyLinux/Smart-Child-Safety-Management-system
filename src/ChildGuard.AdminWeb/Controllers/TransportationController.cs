using ChildGuard.Domain.Enums;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class TransportationController : BaseAdminController
{
    public TransportationController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Transportation", "Transport");

        var buses = await Api.GetAsync<List<BusDto>>("/api/transportation/buses", ct);
        if (!buses.Succeeded) ViewBag.LoadError = buses.Error;

        var routes = await Api.GetAsync<List<RouteDto>>("/api/transportation/routes", ct);
        var drivers = await Api.GetAsync<List<DriverDto>>("/api/transportation/drivers", ct);
        var assignments = await Api.GetAsync<List<StudentBusAssignmentDto>>("/api/transportation/assignments", ct);
        var students = await Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var settings = await Api.GetAsync<TransportSettingsDto>("/api/transportation/settings", ct);
        var audit = await Api.GetAsync<List<TransportAuditDto>>("/api/transportation/audit?take=15", ct);

        ViewBag.Routes = routes.Value ?? new();
        ViewBag.Drivers = drivers.Value ?? new();
        ViewBag.Assignments = assignments.Value ?? new();
        ViewBag.Students = students.Value ?? new();
        ViewBag.Settings = settings.Value ?? new TransportSettingsDto(false);
        ViewBag.Audit = audit.Value ?? new();
        return View(buses.Value ?? new());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDriver(string fullName, string? phone, string? licenseNumber, CancellationToken ct)
    {
        var result = await Api.PostAsync<DriverDto>("/api/transportation/drivers", new CreateDriverRequest(fullName, phone, licenseNumber), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Driver {fullName} added." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoute(string routeName, string? description, CancellationToken ct)
    {
        var result = await Api.PostAsync<RouteDto>("/api/transportation/routes", new CreateRouteRequest(routeName, description), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Route {routeName} added." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBus(string busNumber, int capacity, int? driverId, int? routeId, CancellationToken ct)
    {
        var result = await Api.PostAsync<BusDto>("/api/transportation/buses", new CreateBusRequest(busNumber, capacity, driverId, routeId), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? $"Bus {busNumber} added." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAssignment(int studentId, int busId, int? routeId, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(studentId, busId, routeId), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Student assigned to bus." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    // ---- Edit and status. Nothing is deleted: records are deactivated, so the
    //      journeys and assignments that refer to them stay intact. ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDriver(int id, string fullName, string? phone, string? licenseNumber, CancellationToken ct) =>
        Done(await Api.PutAsync<object?>($"/api/transportation/drivers/{id}", new UpdateDriverRequest(fullName, phone, licenseNumber), ct),
            $"Driver {fullName} updated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DriverStatus(int id, RecordStatus status, CancellationToken ct) =>
        Done(await Api.PatchAsync<object?>($"/api/transportation/drivers/{id}/status", new UpdateTransportStatusRequest(status), ct),
            status == RecordStatus.Active ? "Driver reactivated." : "Driver deactivated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRoute(int id, string routeName, string? description, CancellationToken ct) =>
        Done(await Api.PutAsync<object?>($"/api/transportation/routes/{id}", new UpdateRouteRequest(routeName, description), ct),
            $"Route {routeName} updated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RouteStatus(int id, RecordStatus status, CancellationToken ct) =>
        Done(await Api.PatchAsync<object?>($"/api/transportation/routes/{id}/status", new UpdateTransportStatusRequest(status), ct),
            status == RecordStatus.Active ? "Route reactivated." : "Route deactivated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBus(int id, string busNumber, int capacity, int? driverId, int? routeId, CancellationToken ct) =>
        Done(await Api.PutAsync<object?>($"/api/transportation/buses/{id}", new UpdateBusRequest(busNumber, capacity, driverId, routeId), ct),
            $"Bus {busNumber} updated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BusStatus(int id, RecordStatus status, CancellationToken ct) =>
        Done(await Api.PatchAsync<object?>($"/api/transportation/buses/{id}/status", new UpdateTransportStatusRequest(status), ct),
            status == RecordStatus.Active ? "Bus returned to service." : "Bus taken out of service.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndAssignment(int id, CancellationToken ct) =>
        Done(await Api.PatchAsync<object?>($"/api/transportation/assignments/{id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), ct),
            "Bus assignment ended. The student is no longer on that bus.");

    private IActionResult Done<T>(ApiResult<T> result, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToAction(nameof(Index));
    }
}
