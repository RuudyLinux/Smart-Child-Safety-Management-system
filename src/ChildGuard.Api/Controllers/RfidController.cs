using ChildGuard.Domain.Enums;
using ChildGuard.Application.Common;
using ChildGuard.Api.Common;
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
    public async Task<IActionResult> GetCards(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] bool? assigned,
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? dir,
        CancellationToken ct)
    {
        var paging = PageRequest.From(page, pageSize, sort, dir, ListSorts.RfidCards);
        if (!paging.Succeeded) return BadRequest(new { message = paging.Error });
        if (paging.Value is null) return Ok(await _rfidService.GetAllAsync(ct));   // without page: the whole list, as before

        RfidCardStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RfidCardStatus>(status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(new { message = "status must be Active, Inactive, Lost, Damaged or Retired." });
            statusFilter = parsed;
        }
        var result = await _rfidService.GetPageAsync(new RfidCardListFilter(search, statusFilter, assigned), paging.Value, ct);
        PagingHeaders.SetTotal(Response, result.Total);
        return Ok(result.Items);
    }

    /// <summary>School-wide card figures for the list header, in one call.</summary>
    [HttpGet("cards/summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetCardSummary(CancellationToken ct) => Ok(await _rfidService.GetSummaryAsync(ct));

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

    // ---- inventory

    private IActionResult Op(ChildGuard.Application.Common.Result<RfidOperationResultDto> r) => r.Succeeded
        ? Ok(r.Value)
        : r.Error == ChildGuard.Application.Services.RfidService.CardNotFound ? NotFound(new { message = r.Error }) : BadRequest(new { message = r.Error });

    [HttpPost("cards/register")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Register(RegisterRfidCardRequest request, [FromServices] IValidator<RegisterRfidCardRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        return Op(await _rfidService.RegisterAsync(request, this.GetUserId(), ct));
    }

    [HttpPost("cards/{id:int}/assign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AssignRegistered(int id, AssignRegisteredCardRequest request, [FromServices] IValidator<AssignRegisteredCardRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        return Op(await _rfidService.AssignRegisteredAsync(id, request, this.GetUserId(), ct));
    }

    [HttpPost("cards/{id:int}/unassign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Unassign(int id, UnassignRfidRequest request, [FromServices] IValidator<UnassignRfidRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        return Op(await _rfidService.UnassignAsync(id, request, this.GetUserId(), ct));
    }

    [HttpPost("cards/{id:int}/replace")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Replace(int id, ReplaceRfidRequest request, [FromServices] IValidator<ReplaceRfidRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        return Op(await _rfidService.ReplaceAsync(id, request, this.GetUserId(), ct));
    }

    [HttpPost("cards/{id:int}/reassign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reassign(int id, ReassignRfidRequest request, [FromServices] IValidator<ReassignRfidRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        return Op(await _rfidService.ReassignAsync(id, request, this.GetUserId(), ct));
    }

    /// <summary>Permanent removal of a card that was never used (409 otherwise: retire it instead).</summary>
    [HttpDelete("cards/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DeleteCard(int id, [FromBody] DeleteRecordRequest request, [FromServices] IValidator<DeleteRecordRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(request, ct);
        var r = await _rfidService.DeleteAsync(id, request.Reason, this.GetUserId(), ct);
        if (r.Succeeded) return Ok(r.Value);
        return r.Error == ChildGuard.Application.Services.RfidService.CardNotFound ? NotFound(new { message = r.Error }) : Conflict(new { message = r.Error });
    }

    /// <summary>Can this UID be given to a student? POST so the UID stays out of URLs and access logs.</summary>
    [HttpPost("cards/check")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CheckCard(RfidCardCheckRequest request, [FromServices] IValidator<RfidCardCheckRequest> validator, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        return Ok(await _rfidService.CheckCardAsync(request.CardUid, ct));
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
    /// Pickup desk, automatic mode: which checkpoint the system would record for this card now, decided from
    /// today's School Time schedule and the student's journey so far. Records nothing.
    /// </summary>
    [HttpPost("next-checkpoint")]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting(RateLimitPolicies.RfidScan)]
    public async Task<IActionResult> NextCheckpoint(RfidIdentifyRequest request, CancellationToken ct)
    {
        await _identifyValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await _rfidService.ResolveCheckpointAsync(request, ct));
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
