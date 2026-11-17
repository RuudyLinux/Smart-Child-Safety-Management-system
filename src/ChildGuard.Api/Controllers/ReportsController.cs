using ChildGuard.Domain.Enums;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Common;
using ChildGuard.Api.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>Read-oriented report endpoints — thin wrappers over existing query services, filterable by date.</summary>
[ApiController]
[Route("api/reports")]
[Authorize(Policy = "AdminOnly")]
public class ReportsController : ControllerBase
{
    private readonly IAttendanceJourneyService _attendanceJourney;
    private readonly IVerificationService _verification;

    public ReportsController(IAttendanceJourneyService attendanceJourney, IVerificationService verification)
    {
        _attendanceJourney = attendanceJourney;
        _verification = verification;
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> AttendanceReport([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? studentId, CancellationToken ct) =>
        Ok(await _attendanceJourney.GetAttendanceAsync(studentId, from, to, ct));

    [HttpGet("journey")]
    public async Task<IActionResult> JourneyReport([FromQuery] int studentId, CancellationToken ct) =>
        Ok(await _attendanceJourney.GetJourneyHistoryAsync(studentId, ct));

    /// <summary>
    /// Date-bounded, paged journey events (UTC dates, inclusive). Response carries
    /// totalCount/totalPages/hasMore so a client never mistakes one page for the
    /// whole range. Defaults: last 30 days, page 1, 50 per page (max 200).
    /// </summary>
    [HttpGet("journey/events")]
    public async Task<IActionResult> JourneyEvents(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? studentId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var rangeTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var rangeFrom = from ?? rangeTo.AddDays(-29);
        var result = await _attendanceJourney.GetJourneyReportAsync(rangeFrom, rangeTo, studentId, page, pageSize, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("verification")]
    public async Task<IActionResult> VerificationReport(
        [FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? search, [FromQuery] string? result, [FromQuery] bool? approved,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? dir,
        CancellationToken ct)
    {
        var paging = PageRequest.From(page, pageSize, sort, dir, ListSorts.VerificationHistory);
        if (!paging.Succeeded) return BadRequest(new { message = paging.Error });
        if (paging.Value is null) return Ok(await _verification.GetHistoryAsync(studentId, from, to, ct));   // without page: as before

        VerificationResult? resultFilter = null;
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (!Enum.TryParse<VerificationResult>(result.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(new { message = "result must be Match or NoMatch." });
            resultFilter = parsed;
        }
        var list = await _verification.GetHistoryPageAsync(new VerificationHistoryFilter(studentId, from, to, search, resultFilter, approved), paging.Value, ct);
        PagingHeaders.SetTotal(Response, list.Total);
        return Ok(list.Items);
    }

    /// <summary>Figures for the verification log header, over the student and date range.</summary>
    [HttpGet("verification/summary")]
    public async Task<IActionResult> VerificationSummary([FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await _verification.GetHistorySummaryAsync(studentId, from, to, ct));
}
