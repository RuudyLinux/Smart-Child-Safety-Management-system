using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// Read-only system/operational settings — distinct from Profile (personal account
/// info + password). Shows the state an admin actually needs at a glance: which
/// checkpoint scanners are configured and their live connection status.
/// </summary>
public class SettingsController : BaseAdminController
{
    private readonly IWebHostEnvironment _env;
    public SettingsController(ApiClient api, IWebHostEnvironment env) : base(api) => _env = env;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await LoadAsync(ct);
        return View();
    }

    /// <summary>
    /// Issues (or rotates) a scanner's own API key and shows it on this response only.
    /// Rendered directly rather than via redirect + TempData so the key never lands in
    /// a cookie; the response is marked no-store so it is not cached either.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueKey(int id, CancellationToken ct)
    {
        var result = await Api.PostAsync<DeviceCredentialDto>($"/api/rfid/devices/{id}/credentials", new { }, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }
        Response.Headers.CacheControl = "no-store";
        ViewBag.IssuedCredential = result.Value;
        await LoadAsync(ct);
        return View(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeKey(int id, CancellationToken ct)
    {
        var result = await Api.DeleteAsync<object?>($"/api/rfid/devices/{id}/credentials", ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Scanner key revoked." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        SetPage("Settings", "System");
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        ViewBag.Devices = devices.Value ?? new();
        ViewBag.Environment = _env.EnvironmentName;
    }
}
