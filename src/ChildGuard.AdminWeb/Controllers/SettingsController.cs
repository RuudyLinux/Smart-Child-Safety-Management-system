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
    private const long BackupSizeLimit = 100L * 1024 * 1024;

    private readonly IWebHostEnvironment _env;
    private readonly LongRunningApiClient _longRunning;

    public SettingsController(ApiClient api, IWebHostEnvironment env, LongRunningApiClient longRunning) : base(api)
    {
        _env = env;
        _longRunning = longRunning;
    }

    // ---- Database: export, import, reset (administrator accounts are never touched) ----

    [HttpGet]
    public async Task<IActionResult> ExportDatabase(CancellationToken ct)
    {
        var result = await _longRunning.GetFileAsync("/api/admin/database/export", $"childguard-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json", ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = $"The backup could not be created: {result.Error}";
            return Redirect("/Settings#database");
        }
        Response.Headers.CacheControl = "no-store";
        return File(result.Value.Data, "application/json", result.Value.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(BackupSizeLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupSizeLimit)]
    public async Task<IActionResult> ImportDatabase(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Choose a backup file to import.";
            return Redirect("/Settings#database");
        }
        await using var stream = file.OpenReadStream();
        var result = await _longRunning.PostFileAsync<DatabaseOperationResultDto>("/api/admin/database/import", stream, file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/json" : file.ContentType, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? result.Value!.Message : result.Error;
        return Redirect("/Settings#database");
    }

    /// <summary>Called by database.js after it has downloaded the backup.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetDatabase([FromBody] DatabaseResetRequest request, CancellationToken ct)
    {
        var result = await _longRunning.PostAsync<DatabaseOperationResultDto>("/api/admin/database/reset", request, ct);
        if (!result.Succeeded) return StatusCode(result.StatusCode is >= 400 ? result.StatusCode : 500, new { message = result.Error });
        TempData["Success"] = result.Value!.Message;
        return Json(new { message = result.Value.Message });
    }

    // The scanner cards and their modals (data-cg-region in Settings/Index.cshtml). The database card is not part of
    // them: its import and reset replace all data and are handled by database.js.
    private static readonly string[] ScannerRegions = { "scanners", "scanner-modals" };

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
        return FromApi(result, "Scanner key revoked.", "/Settings", ScannerRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDevice(string deviceLabel, CancellationToken ct)
    {
        var result = await Api.PostAsync<RfidDeviceDto>("/api/rfid/devices", new CreateRfidDeviceRequest(deviceLabel), ct);
        return FromApi(result, $"Scanner {deviceLabel?.Trim()} added. Issue it a key if it connects on its own; the pickup desk can use it right away.",
            "/Settings", ScannerRegions);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameDevice(int id, string deviceLabel, CancellationToken ct)
    {
        var result = await Api.PutAsync<object?>($"/api/rfid/devices/{id}", new UpdateRfidDeviceRequest(deviceLabel), ct);
        return FromApi(result, $"Scanner renamed to {deviceLabel?.Trim()}.", "/Settings", ScannerRegions);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        SetPage("Settings", "System");
        var devices = await Api.GetAsync<List<RfidDeviceDto>>("/api/rfid/devices", ct);
        ViewBag.Devices = devices.Value ?? new();
        ViewBag.Database = (await Api.GetAsync<DatabaseOverviewDto>("/api/admin/database", ct)).Value;
        ViewBag.Environment = _env.EnvironmentName;
    }
}
