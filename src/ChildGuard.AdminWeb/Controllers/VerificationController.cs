using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Backs the pickup and drop-off desk. A generic USB RFID reader in keyboard-wedge
/// mode types the card UID straight into the page's focused input (real hardware
/// pattern, no simulation), and the browser's own camera captures the guardian's
/// face for verification. Every action here is a thin authenticated proxy to
/// ChildGuard.Api — no business logic lives in AdminWeb.
/// </summary>
public class VerificationController : BaseAdminController
{
    public VerificationController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        SetPage("Pickup & Drop-off", "Safety Operations");
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        var recent = await Api.GetAsync<List<GuardianVerificationHistoryDto>>("/api/reports/verification", ct);
        ViewBag.Devices = devices.Value ?? new();
        ViewBag.RecentVerifications = (recent.Value ?? new()).Take(8).ToList();
        return View();
    }

    /// <summary>
    /// The audit trail behind the desk: every verification attempt, its match score
    /// and the handover decision that followed.
    /// </summary>
    public async Task<IActionResult> History(int? studentId, CancellationToken ct)
    {
        SetPage("Verification Log", "Safety Operations");
        var url = studentId.HasValue ? $"/api/reports/verification?studentId={studentId}" : "/api/reports/verification";
        var history = await Api.GetAsync<List<GuardianVerificationHistoryDto>>(url, ct);
        if (!history.Succeeded) ViewBag.LoadError = history.Error;

        var students = await Api.GetAsync<List<StudentListItemDto>>("/api/students", ct);
        ViewBag.Students = students.Value ?? new();
        ViewData["StudentId"] = studentId;
        return View(history.Value ?? new());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Heartbeat(int deviceId, CancellationToken ct)
    {
        var result = await Api.PostAsync<object?>($"/api/rfid/devices/{deviceId}/heartbeat", new { }, ct);
        return result.Succeeded ? Json(new { connected = true }) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> StudentGuardians(int studentId, CancellationToken ct)
    {
        var result = await Api.GetAsync<StudentDetailDto>($"/api/students/{studentId}", ct);
        if (!result.Succeeded) return StatusCode(result.StatusCode, new { message = result.Error });

        // Only guardians authorised for pickup are offered at the desk, and the
        // photo is served through AdminWeb so no API token reaches the page.
        return Json(result.Value!.Guardians
            .Where(g => g.IsAuthorizedForPickup)
            .Select(g => new
            {
                g.GuardianId,
                g.FullName,
                g.Relationship,
                g.FaceRegistered,
                PhotoUrl = g.HasPhoto ? Url.Action(nameof(GuardiansController.Photo), "Guardians", new { id = g.GuardianId }) : null
            }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan([FromBody] RfidScanRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<RfidScanResultDto>("/api/rfid/scan", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FaceVerify([FromBody] FaceVerifyRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<FaceVerifyResultDto>("/api/verification/face-verify", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve([FromBody] ApprovePickupRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<PickupDecisionResultDto>("/api/verification/approve", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject([FromBody] RejectPickupRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<PickupDecisionResultDto>("/api/verification/reject", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }
}
