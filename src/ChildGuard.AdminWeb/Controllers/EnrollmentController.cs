using ChildGuard.AdminWeb.Models.ViewModels;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// The five-step enrollment wizard. The page keeps the admin's entries in the browser; nothing is
/// written until Submit, which hands everything to the API's single-transaction enrollment.
/// </summary>
public class EnrollmentController : BaseAdminController
{
    private readonly LongRunningApiClient _longRunning;

    public EnrollmentController(ApiClient api, LongRunningApiClient longRunning) : base(api) => _longRunning = longRunning;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Enroll new student", "People");
        var buses = await Api.GetAsync<List<BusDto>>("/api/transportation/buses", ct);
        var settings = await Api.GetAsync<TransportSettingsDto>("/api/transportation/settings", ct);
        var routes = await Api.GetAsync<List<RouteDto>>("/api/transportation/routes", ct);
        if (!buses.Succeeded) ViewBag.LoadError = buses.Error;

        return View(new EnrollmentPageModel(
            // One key per opened wizard: resubmitting (double click, retry after a timeout) returns the same enrollment.
            Guid.NewGuid().ToString("N"),
            (buses.Value ?? new()).Where(b => b.Status == RecordStatus.Active).OrderBy(b => b.BusNumber).ToList(),
            (routes.Value ?? new()).Where(r => r.Status == RecordStatus.Active).OrderBy(r => r.RouteName).ToList(),
            settings.Value?.AllowRouteOverride ?? false));
    }

    [HttpGet]
    public async Task<IActionResult> GuardianLookup(string? q, CancellationToken ct)
    {
        var result = await Api.GetAsync<List<GuardianLookupDto>>($"/api/guardians/lookup?q={Uri.EscapeDataString(q ?? "")}", ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckCard([FromBody] RfidCardCheckRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<RfidCardCheckDto>("/api/rfid/cards/check", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckStudent([FromBody] EnrollmentStudentCheckRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<EnrollmentStudentCheckDto>("/api/enrollments/check-student", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    /// <summary>
    /// Relays the API's answer unchanged: the wizard reads a 409's confirmation details (possible duplicate
    /// student, deactivated guardians). Long-running client: credential emails are sent before the API answers.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] EnrollStudentRequest request, CancellationToken ct)
    {
        var (status, json) = await _longRunning.PostRawAsync("/api/enrollments", request, ct);
        return new ContentResult { StatusCode = status, Content = json, ContentType = "application/json" };
    }
}
