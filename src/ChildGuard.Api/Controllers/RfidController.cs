using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/rfid")]
public class RfidController : ControllerBase
{
    private readonly IRfidService _rfidService;
    private readonly IValidator<AssignRfidRequest> _assignValidator;
    private readonly IValidator<UpdateRfidStatusRequest> _statusValidator;
    private readonly IValidator<RfidScanRequest> _scanValidator;

    public RfidController(
        IRfidService rfidService,
        IValidator<AssignRfidRequest> assignValidator,
        IValidator<UpdateRfidStatusRequest> statusValidator,
        IValidator<RfidScanRequest> scanValidator)
    {
        _rfidService = rfidService;
        _assignValidator = assignValidator;
        _statusValidator = statusValidator;
        _scanValidator = scanValidator;
    }

    [HttpGet("cards")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetCards(CancellationToken ct) => Ok(await _rfidService.GetAllAsync(ct));

    [HttpGet("cards/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetCardById(int id, CancellationToken ct)
    {
        var result = await _rfidService.GetByIdAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPost("cards")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Assign(AssignRfidRequest request, CancellationToken ct)
    {
        await _assignValidator.ValidateAndThrowAsync(request, ct);
        var result = await _rfidService.AssignAsync(request, ct);
        return result.Succeeded ? Ok(new { message = "RFID card assigned." }) : BadRequest(new { message = result.Error });
    }

    [HttpPatch("cards/{id:int}/status")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateRfidStatusRequest request, CancellationToken ct)
    {
        await _statusValidator.ValidateAndThrowAsync(request, ct);
        var result = await _rfidService.UpdateStatusAsync(id, request, ct);
        return result.Succeeded ? Ok(new { message = "Status updated." }) : NotFound(new { message = result.Error });
    }

    [HttpGet("devices")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetDevices(CancellationToken ct) => Ok(await _rfidService.GetDevicesAsync(ct));

    /// <summary>
    /// Real scanners announce themselves on connect; the demo page calls this on load.
    /// Accepts either an Admin session (browser demo path) or a device API key
    /// (X-Device-Key header) so a standalone network scanner can call this directly.
    /// </summary>
    [HttpPost("devices/{id:int}/heartbeat")]
    [Authorize(Policy = "AdminOrDevice")]
    public async Task<IActionResult> Heartbeat(int id, CancellationToken ct)
    {
        var result = await _rfidService.HeartbeatAsync(id, ct);
        return result.Succeeded ? Ok(new { message = "Connected." }) : NotFound(new { message = result.Error });
    }

    /// <summary>
    /// Core scan pipeline: single physical scanner, logical checkpoint selected by caller.
    /// Validates state via JourneyValidationService — repeated/out-of-order scans are rejected, not duplicated.
    /// Accepts either an Admin session or a device API key (see Heartbeat above).
    /// </summary>
    [HttpPost("scan")]
    [Authorize(Policy = "AdminOrDevice")]
    [EnableRateLimiting(RateLimitPolicies.RfidScan)]
    public async Task<IActionResult> Scan(RfidScanRequest request, CancellationToken ct)
    {
        await _scanValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _rfidService.ProcessScanAsync(request, ct));
    }
}
