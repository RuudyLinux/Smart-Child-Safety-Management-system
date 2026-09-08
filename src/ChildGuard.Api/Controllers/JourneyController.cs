using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/journey")]
[Authorize(Policy = "AdminOnly")]
public class JourneyController : ControllerBase
{
    private readonly IAttendanceJourneyService _service;
    public JourneyController(IAttendanceJourneyService service) => _service = service;

    [HttpGet("{studentId:int}")]
    public async Task<IActionResult> GetHistory(int studentId, CancellationToken ct) =>
        Ok(await _service.GetJourneyHistoryAsync(studentId, ct));

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await _service.GetRecentEventsAsync(take, ct));
}
