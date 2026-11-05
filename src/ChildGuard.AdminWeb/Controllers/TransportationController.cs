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

        ViewBag.Routes = routes.Value ?? new();
        ViewBag.Drivers = drivers.Value ?? new();
        ViewBag.Assignments = assignments.Value ?? new();
        ViewBag.Students = students.Value ?? new();
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
}
