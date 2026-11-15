using ChildGuard.Application.Common;
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

    /// <summary>The school's transport policy, so the admin UI can show or lock the route picker.</summary>
    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(_service.GetSettings());

    /// <summary>Most recent administrative transport changes (who, what, when), newest first.</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(await _service.GetAuditAsync(take, ct));

    [HttpGet("drivers")]
    public async Task<IActionResult> GetDrivers(CancellationToken ct) => Ok(await _service.GetDriversAsync(ct));

    [HttpPost("drivers")]
    public async Task<IActionResult> AddDriver(CreateDriverRequest request, CancellationToken ct)
    {
        await _driverValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _service.AddDriverAsync(request, ct, this.GetUserId()));
    }

    [HttpGet("routes")]
    public async Task<IActionResult> GetRoutes(CancellationToken ct) => Ok(await _service.GetRoutesAsync(ct));

    [HttpPost("routes")]
    public async Task<IActionResult> AddRoute(CreateRouteRequest request, CancellationToken ct)
    {
        await _routeValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddRouteAsync(request, ct, this.GetUserId());
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("buses")]
    public async Task<IActionResult> GetBuses(CancellationToken ct) => Ok(await _service.GetBusesAsync(ct));

    [HttpPost("buses")]
    public async Task<IActionResult> AddBus(CreateBusRequest request, CancellationToken ct)
    {
        await _busValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddBusAsync(request, ct, this.GetUserId());
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("assignments")]
    public async Task<IActionResult> GetAssignments(CancellationToken ct) => Ok(await _service.GetAssignmentsAsync(ct));

    [HttpPost("assignments")]
    public async Task<IActionResult> AddAssignment(CreateStudentBusAssignmentRequest request, CancellationToken ct)
    {
        await _assignmentValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.AddAssignmentAsync(request, ct, this.GetUserId());
        return result.Succeeded ? Ok(new { message = "Assignment created." }) : BadRequest(new { message = result.Error });
    }

    // ---- Edit and status. Records are never deleted; they become Inactive. ----

    [HttpPut("drivers/{id:int}")]
    public async Task<IActionResult> UpdateDriver(int id, UpdateDriverRequest request,
        [FromServices] IValidator<UpdateDriverRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.UpdateDriverAsync(id, request, ct, this.GetUserId()), "Driver updated.");
    }

    [HttpPatch("drivers/{id:int}/status")]
    public async Task<IActionResult> SetDriverStatus(int id, UpdateTransportStatusRequest request,
        [FromServices] IValidator<UpdateTransportStatusRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.SetDriverStatusAsync(id, request.Status, ct, this.GetUserId()), "Driver status updated.");
    }

    [HttpPut("routes/{id:int}")]
    public async Task<IActionResult> UpdateRoute(int id, UpdateRouteRequest request,
        [FromServices] IValidator<UpdateRouteRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.UpdateRouteAsync(id, request, ct, this.GetUserId()), "Route updated.");
    }

    [HttpPatch("routes/{id:int}/status")]
    public async Task<IActionResult> SetRouteStatus(int id, UpdateTransportStatusRequest request,
        [FromServices] IValidator<UpdateTransportStatusRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.SetRouteStatusAsync(id, request.Status, ct, this.GetUserId()), "Route status updated.");
    }

    [HttpPut("buses/{id:int}")]
    public async Task<IActionResult> UpdateBus(int id, UpdateBusRequest request,
        [FromServices] IValidator<UpdateBusRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.UpdateBusAsync(id, request, ct, this.GetUserId()), "Bus updated.");
    }

    [HttpPatch("buses/{id:int}/status")]
    public async Task<IActionResult> SetBusStatus(int id, UpdateTransportStatusRequest request,
        [FromServices] IValidator<UpdateTransportStatusRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.SetBusStatusAsync(id, request.Status, ct, this.GetUserId()), "Bus status updated.");
    }

    [HttpPatch("assignments/{id:int}/status")]
    public async Task<IActionResult> SetAssignmentStatus(int id, UpdateTransportStatusRequest request,
        [FromServices] IValidator<UpdateTransportStatusRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Answer(await _service.SetAssignmentStatusAsync(id, request.Status, ct, this.GetUserId()), "Assignment status updated.");
    }

    // 404 for a missing record (its error ends in "not found."), 400 for any other refusal.
    private IActionResult Answer(Result result, string success) =>
        result.Succeeded ? Ok(new { message = success })
        : result.Error!.EndsWith("not found.", StringComparison.Ordinal) ? NotFound(new { message = result.Error })
        : BadRequest(new { message = result.Error });
}
