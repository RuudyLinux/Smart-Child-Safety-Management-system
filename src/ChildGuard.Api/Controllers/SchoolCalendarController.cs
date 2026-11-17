using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// School time management: the regular week (school days, start, late cutoff, missing
/// check-in, dismissal, pickup grace) and calendar exceptions (holidays, half days,
/// special timings, extra working days). Changes are admin-only; reading today's
/// timings and the calendar is open to any signed-in user (admins and guardians).
/// </summary>
[ApiController]
[Route("api/school-calendar")]
[Authorize]
public class SchoolCalendarController : ControllerBase
{
    private readonly ISchoolCalendarService _service;

    public SchoolCalendarController(ISchoolCalendarService service) => _service = service;

    /// <summary>Today at the school: open or closed, and its timings.</summary>
    [HttpGet("today")]
    [Authorize(Roles = "Admin,Guardian")]
    public async Task<IActionResult> GetToday(CancellationToken ct) => Ok(await _service.GetTodayAsync(ct));

    /// <summary>Every date in the inclusive range, resolved. At most 400 days.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Guardian")]
    public async Task<IActionResult> GetCalendar([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var result = await _service.GetCalendarAsync(from, to, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("settings")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetSettings(CancellationToken ct) => Ok(await _service.GetSettingsAsync(ct));

    [HttpPut("settings")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateSettings(UpdateSchoolTimingSettingsRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateSettingsAsync(request, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("days")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetExceptions([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _service.GetExceptionsAsync(from, to, includeInactive, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("days")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateException(SchoolCalendarDayRequest request, CancellationToken ct)
    {
        var result = await _service.CreateExceptionAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("days/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateException(int id, SchoolCalendarDayRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateExceptionAsync(id, request, ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Error == SchoolCalendarService.ExceptionNotFound
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>Removes an exception (sets it Inactive; the row is kept as history).</summary>
    [HttpPost("days/{id:int}/deactivate")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeactivateException(int id, CancellationToken ct)
    {
        var result = await _service.DeactivateExceptionAsync(id, ct);
        if (result.Succeeded) return Ok(new { message = "Calendar day removed." });
        return result.Error == SchoolCalendarService.ExceptionNotFound
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }
}
