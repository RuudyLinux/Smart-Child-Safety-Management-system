using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/transportation")]
[Authorize(Policy = "AdminOnly")]
public class TransportationController : ControllerBase
{
    private readonly ITransportationService _service;
    private readonly IValidator<CreateDriverRequest> _driverValidator;
    private readonly IValidator<CreateRouteRequest> _routeValidator;
    private readonly IValidator<CreateBusRequest> _busValidator;
    private readonly IValidator<CreateStudentBusAssignmentRequest> _assignmentValidator;

    public TransportationController(
        ITransportationService service,
        IValidator<CreateDriverRequest> driverValidator,
        IValidator<CreateRouteRequest> routeValidator,
        IValidator<CreateBusRequest> busValidator,
        IValidator<CreateStudentBusAssignmentRequest> assignmentValidator)
    {
        _service = service;
        _driverValidator = driverValidator;
        _routeValidator = routeValidator;
        _busValidator = busValidator;
        _assignmentValidator = assignmentValidator;
    }

    [HttpGet("drivers")]
    public async Task<IActionResult> GetDrivers(CancellationToken ct) => Ok(await _service.GetDriversAsync(ct));

    [HttpPost("drivers")]
    public async Task<IActionResult> AddDriver(CreateDriverRequest request, CancellationToken ct)
    {
        await _driverValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.AddDriverAsync(request, ct));
    }

    [HttpGet("routes")]
    public async Task<IActionResult> GetRoutes(CancellationToken ct) => Ok(await _service.GetRoutesAsync(ct));

    [HttpPost("routes")]
    public async Task<IActionResult> AddRoute(CreateRouteRequest request, CancellationToken ct)
    {
        await _routeValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddRouteAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("buses")]
    public async Task<IActionResult> GetBuses(CancellationToken ct) => Ok(await _service.GetBusesAsync(ct));

    [HttpPost("buses")]
    public async Task<IActionResult> AddBus(CreateBusRequest request, CancellationToken ct)
    {
        await _busValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddBusAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments(CancellationToken ct) => Ok(await _service.GetAssignmentsAsync(ct));

    [HttpPost("assignments")]
    public async Task<IActionResult> AddAssignment(CreateStudentBusAssignmentRequest request, CancellationToken ct)
    {
        await _assignmentValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddAssignmentAsync(request, ct);
        return result.Succeeded ? Ok(new { message = "Assignment created." }) : BadRequest(new { message = result.Error });
    }
}
