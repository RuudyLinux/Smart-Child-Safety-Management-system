using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.Domain.Enums;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

public class TransportationController : BaseAdminController
{
    public TransportationController(ApiClient api) : base(api) { }

    /// <summary>
    /// The fleet console. The assignments list (every assignment ever made, so it only grows) is paged, filtered and
    /// sorted by the API with its state in the URL; a change to it re-renders only the "list" region, which needs the
    /// buses and routes (bus route shown, change-bus pickers) but not the driver list, students or audit.
    /// Buses, drivers and routes are bounded by the size of the fleet and stay whole lists.
    /// </summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Transportation", "Transport");
        var query = new ListQuery(ListDefinitions.Assignments, Request.Query);

        var busesTask = Api.GetAsync<List<BusDto>>("/api/transportation/buses", ct);
        var routesTask = Api.GetAsync<List<RouteDto>>("/api/transportation/routes", ct);
        var settingsTask = Api.GetAsync<TransportSettingsDto>("/api/transportation/settings", ct);
        var assignmentsTask = Api.GetPagedAsync<StudentBusAssignmentDto>("/api/transportation/assignments?" + query.ToApiQuery(), ct);
        var listOnly = ListRegionOnly;
        var driversTask = listOnly ? null : Api.GetAsync<List<DriverDto>>("/api/transportation/drivers", ct);
        var studentsTask = listOnly ? null : Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        var auditTask = listOnly ? null : Api.GetAsync<List<TransportAuditDto>>("/api/transportation/audit?take=15", ct);
        await Task.WhenAll(new Task?[] { busesTask, routesTask, settingsTask, assignmentsTask, driversTask, studentsTask, auditTask }
            .Where(t => t is not null)!);

        var buses = busesTask.Result;
        if (!buses.Succeeded) ViewBag.LoadError = buses.Error;
        var assignments = assignmentsTask.Result;
        ViewBag.ListError = assignments.Succeeded ? null : assignments.Error;
        var page = assignments.Value ?? new ChildGuard.Application.Common.Paged<StudentBusAssignmentDto>(Array.Empty<StudentBusAssignmentDto>(), 0);

        ViewBag.Routes = routesTask.Result.Value ?? new();
        ViewBag.Drivers = driversTask?.Result.Value ?? new();
        ViewBag.Students = studentsTask?.Result.Value ?? new();
        ViewBag.Settings = settingsTask.Result.Value ?? new TransportSettingsDto(false);
        ViewBag.Audit = auditTask?.Result.Value ?? new();
        ViewBag.Assignments = new ListPageViewModel<StudentBusAssignmentDto>(page.Items, page.Total, query.CurrentPage, query.PageSize);
        ViewData["Query"] = query;
        return View(buses.Value ?? new());
    }

    // ---- driver and bus pages

    public async Task<IActionResult> Driver(int id, CancellationToken ct)
    {
        var r = await Api.GetAsync<DriverDetailDto>($"/api/transportation/drivers/{id}", ct);
        if (!r.Succeeded) return NotFound();
        SetPage(r.Value!.Driver.FullName, "Transport");
        return View(r.Value);
    }

    public async Task<IActionResult> Bus(int id, CancellationToken ct)
    {
        var r = await Api.GetAsync<BusDetailDto>($"/api/transportation/buses/{id}", ct);
        if (!r.Succeeded) return NotFound();
        SetPage($"Bus {r.Value!.Bus.BusNumber}", "Transport");
        return View(r.Value);
    }

    public async Task<IActionResult> Route(int id, CancellationToken ct)
    {
        var r = await Api.GetAsync<RouteDetailDto>($"/api/transportation/routes/{id}", ct);
        if (!r.Succeeded) return NotFound();
        SetPage(r.Value!.Route.RouteName, "Transport");
        return View(r.Value);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRouteDetails(int id, UpdateRouteDetailsRequest request, CancellationToken ct)
    {
        return FromApi(await Api.PutAsync<object?>($"/api/transportation/routes/{id}/details", request, ct), "Route saved.", RouteUrl(id), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStop(int id, RouteStopRequest request, CancellationToken ct)
    {
        var r = await Api.PostAsync<RouteStopDto>($"/api/transportation/routes/{id}/stops", request, ct);
        return FromApi(r, r.Succeeded ? $"Stop {r.Value!.Name} added." : "", RouteUrl(id), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditStop(int id, int stopId, RouteStopRequest request, CancellationToken ct)
    {
        return FromApi(await Api.PutAsync<object?>($"/api/transportation/routes/{id}/stops/{stopId}", request, ct), "Stop saved.", RouteUrl(id), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveStop(int id, int stopId, CancellationToken ct)
    {
        var r = await Api.DeleteAsync<MessageDto>($"/api/transportation/routes/{id}/stops/{stopId}", ct);
        return FromApi(r, r.Succeeded ? r.Value!.Message : "", RouteUrl(id), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReactivateStop(int id, int stopId, CancellationToken ct)
    {
        return FromApi(await Api.PatchAsync<object?>($"/api/transportation/routes/{id}/stops/{stopId}/status", new UpdateTransportStatusRequest(RecordStatus.Active), ct),
            "Stop back in use.", RouteUrl(id), RoutePage);
    }

    /// <summary>Moves one stop up or down by sending the whole new order of active stops.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveStop(int id, int stopId, string direction, CancellationToken ct)
    {
        var detail = await Api.GetAsync<RouteDetailDto>($"/api/transportation/routes/{id}", ct);
        if (!detail.Succeeded) return FromApi(detail, "", "/Transportation");
        var order = detail.Value!.Stops.Where(s => s.Status == RecordStatus.Active).OrderBy(s => s.Sequence).Select(s => s.RouteStopId).ToList();
        var i = order.IndexOf(stopId);
        var j = direction == "up" ? i - 1 : i + 1;
        if (i < 0 || j < 0 || j >= order.Count) return Done("Stop order unchanged.", RouteUrl(id, "stops"), RoutePage);   // already first or last
        (order[i], order[j]) = (order[j], order[i]);
        return FromApi(await Api.PutAsync<object?>($"/api/transportation/routes/{id}/stops/order", new ReorderStopsRequest(order), ct),
            "Stop order saved.", RouteUrl(id, "stops"), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAssignmentStops(int id, int assignmentId, int? pickupStopId, int? dropStopId, CancellationToken ct)
    {
        return FromApi(await Api.PatchAsync<object?>($"/api/transportation/assignments/{assignmentId}/stops", new UpdateAssignmentStopsRequest(pickupStopId, dropStopId), ct),
            "Stops saved.", RouteUrl(id, "students"), RoutePage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoute(int id, string? reason, bool confirmed, CancellationToken ct) =>
        await DeleteAsync($"/api/transportation/routes/{id}", reason, confirmed, nameof(Route), id, ct);

    public sealed record MessageDto(string Message);

    private static string RouteUrl(int id, string? tab = null) => $"/Transportation/Route/{id}" + (tab is null ? "" : "#" + tab);
    private static string DriverUrl(int id) => $"/Transportation/Driver/{id}";
    private static string BusUrl(int id) => $"/Transportation/Bus/{id}";

    // Page parts each kind of change affects (data-cg-region names in the Transportation views): every page is one
    // region, so what a change reaches (counts, selects, history) is always current.
    private static readonly string[] Console = { "fleet-console" };
    private static readonly string[] RoutePage = { "route-page" };
    private static readonly string[] DriverPage = { "driver-page" };
    private static readonly string[] BusPage = { "bus-page" };

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDriverProfile(int id, UpdateDriverProfileRequest request, CancellationToken ct)
    {
        return FromApi(await Api.PutAsync<object?>($"/api/transportation/drivers/{id}/profile", request, ct), "Driver profile saved.", DriverUrl(id), DriverPage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBusDetails(int id, UpdateBusDetailsRequest request, CancellationToken ct)
    {
        return FromApi(await Api.PutAsync<object?>($"/api/transportation/buses/{id}/details", request, ct), "Bus details saved.", BusUrl(id), BusPage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBusOperationalStatus(int id, ChildGuard.Domain.Enums.BusStatus status, string? reason, CancellationToken ct)
    {
        return FromApi(await Api.PatchAsync<object?>($"/api/transportation/buses/{id}/operational-status", new BusOperationalStatusRequest(status, reason), ct),
            $"Bus status set to {BusStatusText.Label(status).ToLowerInvariant()}.", BusUrl(id), BusPage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDriver(int id, string? reason, bool confirmed, CancellationToken ct) =>
        await DeleteAsync($"/api/transportation/drivers/{id}", reason, confirmed, nameof(Driver), id, ct);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBus(int id, string? reason, bool confirmed, CancellationToken ct) =>
        await DeleteAsync($"/api/transportation/buses/{id}", reason, confirmed, nameof(Bus), id, ct);

    private async Task<IActionResult> DeleteAsync(string url, string? reason, bool confirmed, string backAction, int id, CancellationToken ct)
    {
        var back = $"/Transportation/{backAction}/{id}";
        if (!confirmed)
            return Failed("Confirm that you understand the deletion cannot be undone.", 400, back);
        var r = await Api.DeleteAsync<LifecycleResultDto>(url, new DeleteRecordRequest(reason ?? ""), ct);
        // The record is gone: the browser leaves its page for the console.
        return FromApi(r, r.Succeeded ? r.Value!.Message : "", r.Succeeded ? "/Transportation" : back);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDriver(string fullName, string? phone, string? licenseNumber, CancellationToken ct)
    {
        var result = await Api.PostAsync<DriverDto>("/api/transportation/drivers", new CreateDriverRequest(fullName, phone, licenseNumber), ct);
        return FromApi(result, $"Driver {fullName} added.", "/Transportation", Console);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRoute(string routeName, string? description, CancellationToken ct)
    {
        var result = await Api.PostAsync<RouteDto>("/api/transportation/routes", new CreateRouteRequest(routeName, description), ct);
        return FromApi(result, $"Route {routeName} added.", "/Transportation", Console);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBus(string busNumber, int capacity, int? driverId, int? routeId, CancellationToken ct)
    {
        var result = await Api.PostAsync<BusDto>("/api/transportation/buses", new CreateBusRequest(busNumber, capacity, driverId, routeId), ct);
        return FromApi(result, $"Bus {busNumber} added.", "/Transportation", Console);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAssignment(int studentId, int busId, int? routeId, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>("/api/transportation/assignments", new CreateStudentBusAssignmentRequest(studentId, busId, routeId), ct);
        return FromApi(result, "Student assigned to bus.", "/Transportation", Console);
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
    public async Task<IActionResult> BusStatus(int id, ChildGuard.Domain.Enums.BusStatus status, CancellationToken ct) =>
        // The quick toggle on the fleet card: in service, or out of service. Other states are set on the bus page.
        Done(await Api.PatchAsync<object?>($"/api/transportation/buses/{id}/operational-status", new BusOperationalStatusRequest(status, null), ct),
            status == ChildGuard.Domain.Enums.BusStatus.Active ? "Bus returned to service." : "Bus taken out of service.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndAssignment(int id, CancellationToken ct) =>
        Done(await Api.PatchAsync<object?>($"/api/transportation/assignments/{id}/status", new UpdateTransportStatusRequest(RecordStatus.Inactive), ct),
            "Bus assignment ended. The student is no longer on that bus.");

    private IActionResult Done<T>(ApiResult<T> result, string success) => FromApi(result, success, "/Transportation", Console);
}
