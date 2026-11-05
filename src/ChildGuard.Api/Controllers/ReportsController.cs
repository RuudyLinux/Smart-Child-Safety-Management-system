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
        [FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await _verification.GetHistoryAsync(studentId, from, to, ct));
}
