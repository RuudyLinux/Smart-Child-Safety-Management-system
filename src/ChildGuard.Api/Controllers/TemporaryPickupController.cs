using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Temporary pickup guardians. Guardian routes live under api/me and always use the id from the token; admin
/// routes (list, decide, desk scan and handover) live under api/temporary-pickups and require the admin role.
/// </summary>
[ApiController]
public class TemporaryPickupController : ControllerBase
{
    private readonly ITemporaryPickupService _service;
    private readonly IValidator<CreateTemporaryPickupRequest> _createValidator;
    private readonly IValidator<ScanTemporaryPickupRequest> _scanValidator;
    private readonly IValidator<TemporaryPickupHandoverRequest> _handoverValidator;

    public TemporaryPickupController(
        ITemporaryPickupService service,
        IValidator<CreateTemporaryPickupRequest> createValidator,
        IValidator<ScanTemporaryPickupRequest> scanValidator,
        IValidator<TemporaryPickupHandoverRequest> handoverValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _scanValidator = scanValidator;
        _handoverValidator = handoverValidator;
    }

    // ---------------------------------------------------------------- guardian

    [HttpPost("api/me/temporary-pickups")]
    [Authorize(Policy = "GuardianOnly")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> Create(CreateTemporaryPickupRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.CreateAsync(this.GetUserId(), request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("api/me/temporary-pickups")]
    [Authorize(Policy = "GuardianOnly")]
    public async Task<IActionResult> ListMine(CancellationToken ct) => Ok(await _service.ListForGuardianAsync(this.GetUserId(), ct));

    [HttpGet("api/me/temporary-pickups/{id:int}")]
    [Authorize(Policy = "GuardianOnly")]
    public async Task<IActionResult> GetMine(int id, CancellationToken ct)
    {
        var result = await _service.GetForGuardianAsync(this.GetUserId(), id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("api/me/temporary-pickups/{id:int}/photo")]
    [Authorize(Policy = "GuardianOnly")]
    public async Task<IActionResult> GetMyPhoto(int id, CancellationToken ct)
    {
        var result = await _service.GetPhotoForGuardianAsync(this.GetUserId(), id, ct);
        return result.Succeeded ? PhotoFile(result.Value!) : NotFound(new { message = result.Error });
    }

    [HttpGet("api/me/temporary-pickups/{id:int}/qr")]
    [Authorize(Policy = "GuardianOnly")]
    public async Task<IActionResult> GetQr(int id, CancellationToken ct)
    {
        var result = await _service.GetQrAsync(this.GetUserId(), id, ct);
        if (!result.Succeeded) return BadRequest(new { message = result.Error });
        Response.Headers.CacheControl = "no-store";
        return Ok(result.Value);
    }

    [HttpPost("api/me/temporary-pickups/{id:int}/cancel")]
    [Authorize(Policy = "GuardianOnly")]
    public async Task<IActionResult> Cancel(int id, TemporaryPickupDecisionRequest? request, CancellationToken ct)
    {
        var result = await _service.CancelAsync(this.GetUserId(), id, request?.Reason, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // ---------------------------------------------------------------- admin

    [HttpGet("api/temporary-pickups")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> List([FromQuery] TemporaryPickupState? state, [FromQuery] int? studentId, CancellationToken ct) =>
        Ok(await _service.ListForAdminAsync(state, studentId, ct));

    [HttpGet("api/temporary-pickups/summary")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(await _service.GetSummaryAsync(ct));

    [HttpGet("api/temporary-pickups/{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await _service.GetForAdminAsync(id, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpGet("api/temporary-pickups/{id:int}/photo")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var result = await _service.GetPhotoAsync(id, ct);
        return result.Succeeded ? PhotoFile(result.Value!) : NotFound(new { message = result.Error });
    }

    [HttpPost("api/temporary-pickups/{id:int}/approve")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(this.GetUserId(), id, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("api/temporary-pickups/{id:int}/reject")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Reject(int id, TemporaryPickupDecisionRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(this.GetUserId(), id, request.Reason, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("api/temporary-pickups/{id:int}/revoke")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Revoke(int id, TemporaryPickupDecisionRequest request, CancellationToken ct)
    {
        var result = await _service.RevokeAsync(this.GetUserId(), id, request.Reason, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Desk: resolve a scanned QR into the child, the expected person and whether a handover is possible.</summary>
    [HttpPost("api/temporary-pickups/scan")]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    public async Task<IActionResult> Scan(ScanTemporaryPickupRequest request, CancellationToken ct)
    {
        await _scanValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.ScanAsync(this.GetUserId(), request.Token, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Desk: QR token plus a live photo. A face match then records the handover.</summary>
    [HttpPost("api/temporary-pickups/{id:int}/handover")]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> HandOver(int id, TemporaryPickupHandoverRequest request, CancellationToken ct)
    {
        await _handoverValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.HandOverAsync(this.GetUserId(), id, request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    private IActionResult PhotoFile(PhotoContentDto photo)
    {
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Data, photo.ContentType);
    }
}
