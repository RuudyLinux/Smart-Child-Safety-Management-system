using ChildGuard.Application.DTOs;
using ChildGuard.Application.Import;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Bulk import for admins. Upload and validation write nothing; only commit inserts, after validating
/// every row again on the server. Every commit — successful or not — leaves a history row naming the
/// admin, which is the audit trail, and is also logged.
/// </summary>
[ApiController]
[Route("api/imports")]
[Authorize(Policy = "AdminOnly")]
public class ImportsController : ControllerBase
{
    /// <summary>Upload cap: the file limit plus room for the multipart envelope.</summary>
    public const long UploadRequestLimit = ImportLimits.MaxFileBytes + (256 * 1024);
    /// <summary>JSON rows sent back for validation/commit (5000 rows of short text fits well within it).</summary>
    public const long RowsRequestLimit = 16L * 1024 * 1024;

    private readonly IBulkImportService _imports;
    private readonly ILogger<ImportsController> _logger;

    public ImportsController(IBulkImportService imports, ILogger<ImportsController> logger)
    {
        _imports = imports;
        _logger = logger;
    }

    [HttpGet("categories")]
    public IActionResult GetCategories() => Ok(_imports.GetCategories());

    [HttpGet("{category}/template")]
    public IActionResult GetTemplate(ImportCategory category, [FromQuery] string? format)
    {
        var result = _imports.GetTemplate(category, format ?? "xlsx");
        if (!result.Succeeded) return BadRequest(new { message = result.Error });
        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Data, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>Reads an uploaded .xlsx or .csv and returns the validated rows. Nothing is saved.</summary>
    [HttpPost("{category}/preview")]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> PreviewFile(ImportCategory category, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose a file to upload." });
        if (file.Length > ImportLimits.MaxFileBytes)
            return BadRequest(new { message = $"The file is larger than {ImportLimits.MaxFileBytes / (1024 * 1024)} MB. Split it into smaller files." });

        // The declared content type is ignored: the reader decides from the extension and the bytes.
        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var result = await _imports.PreviewFileAsync(category, bytes, file.FileName, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Validates rows from the manual-entry grid or a corrected preview. Nothing is saved.</summary>
    [HttpPost("validate")]
    [RequestSizeLimit(RowsRequestLimit)]
    public async Task<IActionResult> Validate(ValidateImportRequest request, CancellationToken ct)
    {
        var result = await _imports.ValidateAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("commit")]
    [RequestSizeLimit(RowsRequestLimit)]
    public async Task<IActionResult> Commit(CommitImportRequest request, CancellationToken ct)
    {
        var adminId = this.GetUserId();
        var result = await _imports.CommitAsync(request, adminId, ct);
        if (!result.Succeeded) return BadRequest(new { message = result.Error });

        var s = result.Value!;
        _logger.LogInformation(
            "Bulk import {ImportId} by admin {AdminId}: {Category} from {Source}, {Total} rows, {Inserted} inserted, {Failed} failed, status {Status}",
            s.DataImportId, adminId, s.Category, request.Source, s.TotalRows, s.InsertedRows, s.FailedRows, s.Status);
        return Ok(s);
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory([FromQuery] ImportCategory? category, CancellationToken ct) =>
        Ok(await _imports.GetHistoryAsync(category, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var result = await _imports.GetDetailAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("{id:int}/error-report")]
    public async Task<IActionResult> GetErrorReport(int id, [FromQuery] string? format, CancellationToken ct)
    {
        var result = await _imports.GetErrorReportAsync(id, format ?? "csv", ct);
        if (!result.Succeeded)
            return result.Error == ChildGuard.Application.Services.BulkImportService.ImportNotFound ? NotFound(new { message = result.Error }) : BadRequest(new { message = result.Error });
        // Holds personal data from the import: never cached by a shared proxy.
        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Data, result.Value.ContentType, result.Value.FileName);
    }
}
