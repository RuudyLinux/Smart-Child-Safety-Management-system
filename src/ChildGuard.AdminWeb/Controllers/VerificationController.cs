using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Backs the "Student Drop-off & Pickup Verification" demo/operational page.
/// A generic USB RFID reader in keyboard-wedge mode types the card UID straight
/// into the page's focused input (real hardware pattern, no simulation needed);
/// the browser's own camera captures the guardian's face for verification.
/// Every action here is a thin AJAX proxy to ChildGuard.Api — no business logic
/// lives in AdminWeb.
/// </summary>
public class VerificationController : BaseAdminController
{
    public VerificationController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Student Drop-off & Pickup Verification";
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        var recent = await Api.GetAsync<List<GuardianVerificationHistoryDto>>("/api/reports/verification", ct);
        ViewBag.Devices = devices.Value ?? new();
        ViewBag.RecentVerifications = (recent.Value ?? new()).Take(10).ToList();
        return View();
    }

    [HttpPost]
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
        return Json(result.Value!.Guardians.Where(g => g.IsAuthorizedForPickup));
    }

    [HttpPost]
    public async Task<IActionResult> Scan([FromBody] RfidScanRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<RfidScanResultDto>("/api/rfid/scan", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> FaceVerify([FromBody] FaceVerifyRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<FaceVerifyResultDto>("/api/verification/face-verify", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Approve([FromBody] ApprovePickupRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<PickupDecisionResultDto>("/api/verification/approve", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }

    [HttpPost]
    public async Task<IActionResult> Reject([FromBody] RejectPickupRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<PickupDecisionResultDto>("/api/verification/reject", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }
}
