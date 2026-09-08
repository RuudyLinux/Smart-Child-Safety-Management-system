using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/transportation")]
[Authorize(Policy = "AdminOnly")]
public class TransportationController : ControllerBase
{
    private readonly ITransportationService _service;
    public TransportationController(ITransportationService service) => _service = service;

    [HttpGet("drivers")]
    public async Task<IActionResult> GetDrivers(CancellationToken ct) => Ok(await _service.GetDriversAsync(ct));

    [HttpPost("drivers")]
    public async Task<IActionResult> AddDriver(CreateDriverRequest request, CancellationToken ct) =>
        Ok(await _service.AddDriverAsync(request, ct));

    [HttpGet("routes")]
    public async Task<IActionResult> GetRoutes(CancellationToken ct) => Ok(await _service.GetRoutesAsync(ct));

    [HttpPost("routes")]
    public async Task<IActionResult> AddRoute(CreateRouteRequest request, CancellationToken ct) =>
        Ok(await _service.AddRouteAsync(request, ct));

    [HttpGet("buses")]
    public async Task<IActionResult> GetBuses(CancellationToken ct) => Ok(await _service.GetBusesAsync(ct));

    [HttpPost("buses")]
    public async Task<IActionResult> AddBus(CreateBusRequest request, CancellationToken ct)
    {
        var result = await _service.AddBusAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments(CancellationToken ct) => Ok(await _service.GetAssignmentsAsync(ct));

    [HttpPost("assignments")]
    public async Task<IActionResult> AddAssignment(CreateStudentBusAssignmentRequest request, CancellationToken ct)
    {
        var result = await _service.AddAssignmentAsync(request, ct);
        return result.Succeeded ? Ok(new { message = "Assignment created." }) : BadRequest(new { message = result.Error });
    }
}
