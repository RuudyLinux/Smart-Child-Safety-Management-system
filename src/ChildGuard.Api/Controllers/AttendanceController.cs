using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize(Policy = "AdminOnly")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceJourneyService _service;
    public AttendanceController(IAttendanceJourneyService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(await _service.GetAttendanceAsync(studentId, from, to, ct));
}
