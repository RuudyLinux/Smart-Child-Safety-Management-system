using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Whole-database export, import and reset for administrators. Administrator accounts are never part of any of
/// them. The admin site downloads an export before every reset.
/// </summary>
[ApiController]
[Route("api/admin/database")]
[Authorize(Policy = "AdminOnly")]
public class DatabaseController : ControllerBase
{
    /// <summary>Backups include photos, so they are larger than ordinary uploads.</summary>
    public const long BackupSizeLimit = 100L * 1024 * 1024;

    private readonly IDatabaseMaintenanceService _service;
    public DatabaseController(IDatabaseMaintenanceService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await _service.GetOverviewAsync(ct));

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var bytes = await _service.ExportAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return File(bytes, "application/json", $"childguard-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    [HttpPost("import")]
    [RequestSizeLimit(BackupSizeLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = BackupSizeLimit)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose a backup file to import." });
        await using var stream = file.OpenReadStream();
        var result = await _service.ImportAsync(stream, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(DatabaseResetRequest request, CancellationToken ct)
    {
        var result = await _service.ResetAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }
}
