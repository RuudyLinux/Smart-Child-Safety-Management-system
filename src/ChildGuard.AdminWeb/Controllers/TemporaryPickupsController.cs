using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Temporary pickup guardians: the admin queue (approve, reject, revoke), a detail page with the audit trail,
/// and the desk where a QR code is scanned and the collecting person's face is checked. Thin authenticated
/// proxy to ChildGuard.Api; every rule (window, ownership, QR, face match, one-time use) is enforced there.
/// </summary>
public class TemporaryPickupsController : BaseAdminController
{
    public TemporaryPickupsController(ApiClient api) : base(api) { }

    public async Task<IActionResult> Index(TemporaryPickupState? state, CancellationToken ct)
    {
        SetPage("Temporary Pickups", "Safety Operations");
        var list = await Api.GetAsync<List<TemporaryPickupDto>>(
            state is null ? "/api/temporary-pickups" : $"/api/temporary-pickups?state={state}", ct);
        var summary = await Api.GetAsync<TemporaryPickupSummaryDto>("/api/temporary-pickups/summary", ct);
        if (!list.Succeeded) ViewBag.LoadError = list.Error;
        ViewBag.Summary = summary.Value ?? new TemporaryPickupSummaryDto(0, 0, 0, 0);
        ViewData["State"] = state;
        return View(list.Value ?? new());
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        SetPage("Temporary Pickup", "Safety Operations");
        var result = await Api.GetAsync<TemporaryPickupDetailDto>($"/api/temporary-pickups/{id}", ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        return View(result.Value);
    }

    /// <summary>The supplied photo, served through AdminWeb so no API token reaches the page.</summary>
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var photo = await Api.GetBinaryAsync($"/api/temporary-pickups/{id}/photo", ct);
        if (!photo.Succeeded) return NotFound();
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Value.Data, photo.Value.ContentType);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken ct) => await Decide(id, "approve", null, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason, CancellationToken ct) => await Decide(id, "reject", reason, ct);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id, string? reason, CancellationToken ct) => await Decide(id, "revoke", reason, ct);

    private async Task<IActionResult> Decide(int id, string action, string? reason, CancellationToken ct)
    {
        var result = await Api.PostAsync<TemporaryPickupDto>($"/api/temporary-pickups/{id}/{action}", new { reason }, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Temporary pickup {action}{(action.EndsWith('e') ? "d" : "ed")}."
            : result.Error;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------------------------------------------------------- desk

    public IActionResult Desk()
    {
        SetPage("Temporary Pickup Desk", "Safety Operations");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ScanToken([FromBody] ScanTemporaryPickupRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<TemporaryPickupScanResultDto>("/api/temporary-pickups/scan", request, ct);
        if (!result.Succeeded) return StatusCode(result.StatusCode, new { message = result.Error });
        var scan = result.Value!;
        return Json(new
        {
            scan.CanHandOver,
            scan.BlockedReason,
            StudentStatus = scan.StudentStatus.ToString(),
            scan.Pickup.Id,
            scan.Pickup.StudentName,
            scan.Pickup.StudentGrade,
            scan.Pickup.FullName,
            scan.Pickup.Relationship,
            scan.Pickup.Phone,
            scan.Pickup.IdentityNote,
            RequestedBy = scan.Pickup.RequestedByName,
            ValidUntil = scan.Pickup.ValidUntilUtc.ToSchoolTime().ToString("d MMM, HH:mm"),
            PhotoUrl = scan.Pickup.HasPhoto ? Url.Action(nameof(Photo), new { id = scan.Pickup.Id }) : null
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Handover(int id, [FromBody] TemporaryPickupHandoverRequest request, CancellationToken ct)
    {
        var result = await Api.PostAsync<TemporaryPickupHandoverResultDto>($"/api/temporary-pickups/{id}/handover", request, ct);
        return result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
    }
}
