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

        var activeBuses = (buses.Value ?? new()).Where(b => b.Status == BusStatus.Active).OrderBy(b => b.BusNumber).ToList();
        var activeRoutes = (routes.Value ?? new()).Where(r => r.Status == RecordStatus.Active).OrderBy(r => r.RouteName).ToList();
        var allowOverride = settings.Value?.AllowRouteOverride ?? false;

        // Stops of every route a student could ride: the buses' own routes, plus any active route when overrides are allowed.
        var wanted = activeBuses.Where(b => b.RouteId.HasValue).Select(b => b.RouteId!.Value)
            .Concat(allowOverride ? activeRoutes.Select(r => r.RouteId) : [])
            .ToHashSet();
        var stops = new Dictionary<int, List<RouteStopDto>>();
        foreach (var r in (routes.Value ?? new()).Where(r => r.StopCount > 0 && wanted.Contains(r.RouteId)))
        {
            var detail = await Api.GetAsync<RouteDetailDto>($"/api/transportation/routes/{r.RouteId}", ct);
            if (detail.Succeeded)
                stops[r.RouteId] = detail.Value!.Stops.Where(s => s.Status == RecordStatus.Active).OrderBy(s => s.Sequence).ToList();
        }

        return View(new EnrollmentPageModel(
            // One key per opened wizard: resubmitting (double click, retry after a timeout) returns the same enrollment.
            Guid.NewGuid().ToString("N"), activeBuses, activeRoutes, allowOverride, stops));
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

    /// <summary>
    /// The optional photos chosen in the wizard, uploaded after the enrollment has committed (one per call), through
    /// the same API endpoints as the profile pages. A failed upload leaves the enrollment as it is; the photo can be
    /// added from the profile later.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5L * 1024 * 1024)]
    public async Task<IActionResult> Photo(string kind, int id, IFormFile? file, CancellationToken ct)
    {
        var path = kind switch
        {
            "student" => $"/api/students/{id}/photo",
            "guardian" => $"/api/guardians/{id}/photo",
            _ => null
        };
        if (path is null || id <= 0) return BadRequest(new { message = "Unknown photo target." });
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select an image to upload." });

        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<PhotoStateDto>(path, stream, file.FileName, file.ContentType, ct);
        return result.Succeeded ? Json(new { saved = true }) : StatusCode(result.StatusCode, new { message = result.Error });
    }
}
