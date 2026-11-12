using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

/// <summary>
/// Guardian pickup verification: Student Identified → Authorized Guardian → Face Capture →
/// Face Verification → Match? → Approve/Reject → Record → Notification.
/// Backs the "Student Drop-off & Pickup Verification" demo/operational page.
/// </summary>
[ApiController]
[Route("api/verification")]
[Authorize(Policy = "AdminOnly")]
public class VerificationController : ControllerBase
{
    private readonly IVerificationService _service;
    private readonly IValidator<FaceVerifyRequest> _verifyValidator;
    private readonly IValidator<ApprovePickupRequest> _approveValidator;
    private readonly IValidator<RejectPickupRequest> _rejectValidator;
    private readonly IFaceImageAnalysisService _analysis;
    private readonly IValidator<AnalyzeImageRequest> _analyzeValidator;
    private readonly IVerificationSessionService _sessions;
    private readonly IValidator<StartVerificationSessionRequest> _startValidator;
    private readonly IValidator<SessionIdentifyRequest> _identifyValidator;
    private readonly IValidator<LiveVerifyRequest> _liveValidator;

    public VerificationController(
        IVerificationService service,
        IValidator<FaceVerifyRequest> verifyValidator,
        IValidator<ApprovePickupRequest> approveValidator,
        IValidator<RejectPickupRequest> rejectValidator,
        IFaceImageAnalysisService analysis,
        IValidator<AnalyzeImageRequest> analyzeValidator,
        IVerificationSessionService sessions,
        IValidator<StartVerificationSessionRequest> startValidator,
        IValidator<SessionIdentifyRequest> identifyValidator,
        IValidator<LiveVerifyRequest> liveValidator)
    {
        _sessions = sessions;
        _startValidator = startValidator;
        _identifyValidator = identifyValidator;
        _liveValidator = liveValidator;
        _service = service;
        _verifyValidator = verifyValidator;
        _approveValidator = approveValidator;
        _rejectValidator = rejectValidator;
        _analysis = analysis;
        _analyzeValidator = analyzeValidator;
    }

    /// <summary>
    /// Layer 1: is this image fit for recognition (format, size, face found, blur, light, angle)?
    /// Works for webcam captures and gallery uploads alike and records nothing about the image itself.
    /// </summary>
    [HttpPost("analyze-image")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> AnalyzeImage(AnalyzeImageRequest request, CancellationToken ct)
    {
        await _analyzeValidator.ValidateAndThrowAsync(request, ct);
        var result = await _analysis.AnalyzeAsync(request, FaceActor.Admin(this.GetUserId()), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    // ---- Layered verification: quality -> identity -> authorisation -> liveness -> pending approval ----

    /// <summary>Step 1-2: open a verification session for a student and list the guardians the desk may verify.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession(StartVerificationSessionRequest request, CancellationToken ct)
    {
        await _startValidator.ValidateAndThrowAsync(request, ct);
        var result = await _sessions.StartAsync(request.StudentId, this.GetUserId(), ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Error == "Student not found." ? NotFound(new { message = result.Error }) : BadRequest(new { message = result.Error });
    }

    /// <summary>Steps 4-6: quality, identity and authorisation for a webcam still or a gallery image. Never establishes liveness.</summary>
    [HttpPost("sessions/{sessionId:int}/identify")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> IdentifyInSession(int sessionId, SessionIdentifyRequest request, CancellationToken ct)
    {
        await _identifyValidator.ValidateAndThrowAsync(request, ct);
        var result = await _sessions.IdentifyAsync(sessionId, this.GetUserId(), request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Step 7-8: a fresh random head-movement challenge, single use and short lived.</summary>
    [HttpPost("sessions/{sessionId:int}/challenge")]
    [EnableRateLimiting(RateLimitPolicies.LiveVerify)]
    public async Task<IActionResult> IssueChallenge(int sessionId, CancellationToken ct)
    {
        var result = await _sessions.IssueChallengeAsync(sessionId, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    /// <summary>Steps 8-9: the live check. Only a full pass creates a pending verification; an administrator still approves it.</summary>
    [HttpPost("sessions/{sessionId:int}/live-verify")]
    [EnableRateLimiting(RateLimitPolicies.LiveVerify)]
    [RequestSizeLimit(FaceUploads.LiveBurstRequestLimit)]
    public async Task<IActionResult> LiveVerify(int sessionId, LiveVerifyRequest request, CancellationToken ct)
    {
        await _liveValidator.ValidateAndThrowAsync(request, ct);
        var result = await _sessions.LiveVerifyAsync(sessionId, this.GetUserId(), request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("face-verify")]
    public async Task<IActionResult> Verify(FaceVerifyRequest request, CancellationToken ct)
    {
        await _verifyValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.VerifyAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve(ApprovePickupRequest request, CancellationToken ct)
    {
        await _approveValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.ApproveAsync(request, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject(RejectPickupRequest request, CancellationToken ct)
    {
        await _rejectValidator.ValidateAndThrowAsync(request, ct);
        var result = await _service.RejectAsync(request, this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int? studentId, CancellationToken ct) =>
        Ok(await _service.GetHistoryAsync(studentId, ct: ct));
}
