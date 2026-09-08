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

    [HttpGet("verification")]
    public async Task<IActionResult> VerificationReport([FromQuery] int? studentId, CancellationToken ct) =>
        Ok(await _verification.GetHistoryAsync(studentId, ct));
}
