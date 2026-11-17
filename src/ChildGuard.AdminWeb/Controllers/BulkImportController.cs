using System.Text.Json;
using ChildGuard.AdminWeb.Services;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.AdminWeb.Controllers;

/// <summary>
/// The bulk import wizard and import history. The browser talks only to AdminWeb; files and rows
/// are forwarded to the API unchanged, and the API alone decides what is valid and what is saved.
/// </summary>
public class BulkImportController : BaseAdminController
{
    /// <summary>Matches the API's own caps, so an oversized request is refused before it is forwarded.</summary>
    private const long UploadRequestLimit = 5L * 1024 * 1024 + 256 * 1024;
    private const long RowsRequestLimit = 16L * 1024 * 1024;

    private readonly LongRunningApiClient _longRunning;

    public BulkImportController(ApiClient api, LongRunningApiClient longRunning) : base(api) => _longRunning = longRunning;

    public async Task<IActionResult> Index(ImportCategory? category, CancellationToken ct)
    {
        SetPage("Bulk Import", "Data");
        var categories = await Api.GetAsync<List<ImportCategoryDto>>("/api/imports/categories", ct);
        if (!categories.Succeeded) ViewBag.LoadError = categories.Error;
        var history = await Api.GetAsync<List<ImportHistoryItemDto>>("/api/imports", ct);
        ViewBag.RecentImports = (history.Value ?? new()).Take(5).ToList();
        ViewBag.InitialCategory = category?.ToString();
        return View(categories.Value ?? new());
    }

    public async Task<IActionResult> History(ImportCategory? category, CancellationToken ct)
    {
        SetPage("Import History", "Data");
        var url = category is null ? "/api/imports" : $"/api/imports?category={category}";
        var result = await Api.GetAsync<List<ImportHistoryItemDto>>(url, ct);
        if (!result.Succeeded) ViewBag.LoadError = result.Error;
        ViewBag.Category = category;
        return View(result.Value ?? new());
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var result = await Api.GetAsync<ImportDetailDto>($"/api/imports/{id}", ct);
        if (!result.Succeeded) return NotFound();
        SetPage($"Import #{id}", "Data");
        return View(result.Value);
    }

    [HttpGet]
    public async Task<IActionResult> Template(ImportCategory category, string? format, CancellationToken ct)
    {
        var ext = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "xlsx";
        var result = await Api.GetFileAsync($"/api/imports/{category}/template?format={ext}", $"childguard-{category}-template.{ext}".ToLowerInvariant(), ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index), new { category });
        }
        return File(result.Value.Data, result.Value.ContentType, result.Value.FileName);
    }

    [HttpGet]
    public async Task<IActionResult> ErrorReport(int id, string? format, CancellationToken ct)
    {
        var ext = string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase) ? "xlsx" : "csv";
        var result = await Api.GetFileAsync($"/api/imports/{id}/error-report?format={ext}", $"childguard-import-{id}-errors.{ext}", ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id });
        }
        Response.Headers.CacheControl = "no-store";
        return File(result.Value.Data, result.Value.ContentType, result.Value.FileName);
    }

    // ---- JSON endpoints used by bulk-import.js ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> Preview(ImportCategory category, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Choose a file to upload." });
        await using var stream = file.OpenReadStream();
        var result = await Api.PostFileAsync<JsonElement>($"/api/imports/{category}/preview", stream, file.FileName, file.ContentType, ct);
        return Relay(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RowsRequestLimit)]
    public async Task<IActionResult> Validate([FromBody] ValidateImportRequest request, CancellationToken ct) =>
        Relay(await Api.PostAsync<JsonElement>("/api/imports/validate", request, ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RowsRequestLimit)]
    public async Task<IActionResult> Commit([FromBody] CommitImportRequest request, CancellationToken ct) =>
        Relay(await _longRunning.PostAsync<JsonElement>("/api/imports/commit", request, ct));

    private IActionResult Relay(ApiResult<JsonElement> result) =>
        result.Succeeded ? Json(result.Value) : StatusCode(result.StatusCode, new { message = result.Error });
}
