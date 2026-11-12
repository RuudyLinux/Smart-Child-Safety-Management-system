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
    private readonly IValidator<RfidIdentifyRequest> _identifyValidator;

    public RfidController(
        IRfidService rfidService,
        IValidator<AssignRfidRequest> assignValidator,
        IValidator<UpdateRfidStatusRequest> statusValidator,
        IValidator<RfidScanRequest> scanValidator,
        IValidator<RfidIdentifyRequest> identifyValidator)
    {
        _rfidService = rfidService;
        _assignValidator = assignValidator;
        _statusValidator = statusValidator;
        _scanValidator = scanValidator;
        _identifyValidator = identifyValidator;
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

    /// <summary>Registers another checkpoint scanner (a new gate, a second desk, a bus reader).</summary>
    [HttpPost("devices")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateDevice(CreateRfidDeviceRequest request,
        [FromServices] IValidator<CreateRfidDeviceRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var result = await _rfidService.CreateDeviceAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPut("devices/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RenameDevice(int id, UpdateRfidDeviceRequest request,
        [FromServices] IValidator<UpdateRfidDeviceRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var result = await _rfidService.RenameDeviceAsync(id, request, ct);
        if (result.Succeeded) return Ok(new { message = "Scanner renamed." });
        return result.Error == ChildGuard.Application.Services.RfidService.DeviceNotFound
            ? NotFound(new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Real scanners announce themselves on connect; the demo page calls this on load.
    /// Accepts either an Admin session (browser demo path) or a device API key
    /// (X-Device-Key header) so a standalone network scanner can call this directly.
    /// </summary>
    [HttpPost("devices/{id:int}/heartbeat")]
    [Authorize(Policy = "AdminOrDevice")]
    public async Task<IActionResult> Heartbeat(int id, CancellationToken ct)
    {
        if (!CallerMayActAsDevice(id)) return Forbid();
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
        if (!CallerMayActAsDevice(request.RfidDeviceId)) return Forbid();
        return Ok(await _rfidService.ProcessScanAsync(request, ct));
    }

    /// <summary>
    /// Pickup desk: identifies the student on a card without recording anything.
    /// At a pickup checkpoint the departure is written only by the handover approval,
    /// after the collecting guardian has been verified.
    /// </summary>
    [HttpPost("identify")]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting(RateLimitPolicies.RfidScan)]
    public async Task<IActionResult> Identify(RfidIdentifyRequest request, CancellationToken ct)
    {
        await _identifyValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _rfidService.IdentifyAsync(request, ct));
    }

    /// <summary>
    /// Issues (or rotates) the scanner's own API key. The key is in this response only —
    /// store it on the device; it cannot be retrieved again. Rotating invalidates the old key.
    /// </summary>
    [HttpPost("devices/{id:int}/credentials")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> IssueCredential(int id, CancellationToken ct)
    {
        var result = await _rfidService.IssueDeviceKeyAsync(id, ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });
        Response.Headers.CacheControl = "no-store";
        return Ok(result.Value);
    }

    [HttpDelete("devices/{id:int}/credentials")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> RevokeCredential(int id, CancellationToken ct)
    {
        var result = await _rfidService.RevokeDeviceKeyAsync(id, ct);
        return result.Succeeded ? Ok(new { message = "Scanner key revoked." }) : NotFound(new { message = result.Error });
    }

    /// <summary>
    /// A scanner holding its own key may only act as itself. Admins (browser demo) and
    /// the deprecated shared key carry no device_id claim and are not restricted.
    /// </summary>
    private bool CallerMayActAsDevice(int deviceId)
    {
        var claim = User.FindFirst(ApiKeyAuthenticationHandler.DeviceIdClaim)?.Value;
        return claim is null || claim == deviceId.ToString();
    }
}
