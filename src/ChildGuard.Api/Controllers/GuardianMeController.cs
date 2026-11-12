using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

/// <summary>Self-service endpoints for the authenticated Guardian — backs the Flutter app.</summary>
[ApiController]
[Route("api/me")]
[Authorize(Policy = "GuardianOnly")]
public class GuardianMeController : ControllerBase
{
    private readonly IGuardianService _guardianService;
    private readonly IPhotoService _photos;
    private readonly IAuthService _authService;
    private readonly IAttendanceJourneyService _attendanceJourney;
    private readonly INotificationService _notificationService;
    private readonly IVerificationService _verificationService;
    private readonly IValidator<UpdateProfileRequest> _profileValidator;
    private readonly IValidator<RegisterDeviceTokenRequest> _deviceTokenValidator;
    private readonly IValidator<RegisterFaceRequest> _registerFaceValidator;
    private readonly IGuardianFaceReferenceService _faceReferences;
    private readonly IFaceImageAnalysisService _faceAnalysis;
    private readonly IValidator<AddFaceReferenceRequest> _addReferenceValidator;
    private readonly IValidator<AnalyzeImageRequest> _analyzeValidator;
    private readonly IGuardianPortalReadService _portal;

    public GuardianMeController(
        IGuardianService guardianService,
        IPhotoService photos,
        IAuthService authService,
        IAttendanceJourneyService attendanceJourney,
        INotificationService notificationService,
        IVerificationService verificationService,
        IValidator<UpdateProfileRequest> profileValidator,
        IValidator<RegisterDeviceTokenRequest> deviceTokenValidator,
        IValidator<RegisterFaceRequest> registerFaceValidator,
        IGuardianFaceReferenceService faceReferences,
        IFaceImageAnalysisService faceAnalysis,
        IValidator<AddFaceReferenceRequest> addReferenceValidator,
        IValidator<AnalyzeImageRequest> analyzeValidator,
        IGuardianPortalReadService portal)
    {
        _portal = portal;
        _faceReferences = faceReferences;
        _faceAnalysis = faceAnalysis;
        _addReferenceValidator = addReferenceValidator;
        _analyzeValidator = analyzeValidator;
        _guardianService = guardianService;
        _photos = photos;
        _authService = authService;
        _attendanceJourney = attendanceJourney;
        _notificationService = notificationService;
        _verificationService = verificationService;
        _profileValidator = profileValidator;
        _deviceTokenValidator = deviceTokenValidator;
        _registerFaceValidator = registerFaceValidator;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await _guardianService.GetByIdAsync(this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        await _profileValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.UpdateProfileAsync(this.GetUserId(), request, ct);
        return result.Succeeded ? Ok(new { message = "Profile updated." }) : BadRequest(new { message = result.Error });
    }

    [HttpGet("students/{studentId:int}/attendance")]
    public async Task<IActionResult> GetStudentAttendance(
        int studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!await OwnsStudentAsync(studentId, ct)) return Forbid();
        return Ok(await _attendanceJourney.GetAttendanceAsync(studentId, from, to, ct));
    }

    [HttpGet("students/{studentId:int}/journey")]
    public async Task<IActionResult> GetStudentJourney(
        int studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!await OwnsStudentAsync(studentId, ct)) return Forbid();
        // No range: the original full history, unchanged. With a range: school-calendar dates, inclusive.
        if (from is null && to is null)
            return Ok(await _attendanceJourney.GetJourneyHistoryAsync(studentId, ct));
        var end = to ?? from!.Value;
        var start = from ?? end;
        if (start > end) return BadRequest(new { message = "The start date must be on or before the end date." });
        return Ok(await _portal.GetJourneyAsync(this.GetUserId(), studentId, start, end, ct));
    }

    // ---- Guardian app read models: dashboard, student details, transport, reports, pickups ----

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var dashboard = await _portal.GetDashboardAsync(this.GetUserId(), ct);
        return dashboard is null ? NotFound(new { message = "Guardian not found." }) : Ok(dashboard);
    }

    [HttpGet("students")]
    public async Task<IActionResult> GetMyStudents(CancellationToken ct) =>
        Ok(await _portal.GetChildrenAsync(this.GetUserId(), ct));

    [HttpGet("students/{studentId:int}")]
    public async Task<IActionResult> GetStudentDetail(int studentId, CancellationToken ct)
    {
        var detail = await _portal.GetStudentDetailAsync(this.GetUserId(), studentId, ct);
        return detail is null ? Forbid() : Ok(detail);
    }

    [HttpGet("students/{studentId:int}/report")]
    public async Task<IActionResult> GetStudentReport(
        int studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!await _portal.IsLinkedAsync(this.GetUserId(), studentId, ct)) return Forbid();
        var result = await _portal.GetReportAsync(this.GetUserId(), studentId, from, to, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpGet("pickups")]
    public async Task<IActionResult> GetPickups(
        [FromQuery] int? studentId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (studentId is not null && !await _portal.IsLinkedAsync(this.GetUserId(), studentId.Value, ct)) return Forbid();
        return Ok(await _portal.GetPickupsAsync(this.GetUserId(), studentId, from, to, ct));
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken ct) =>
        Ok(await _notificationService.GetForGuardianAsync(this.GetUserId(), ct));

    [HttpPatch("notifications/{id:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken ct)
    {
        var result = await _notificationService.MarkReadAsync(id, this.GetUserId(), ct);
        return result.Succeeded ? Ok(new { message = "Marked read." }) : NotFound(new { message = result.Error });
    }

    // ---- Trusted devices: phones that skip the emailed OTP ----

    /// <summary>
    /// Trusts this phone. The session that calls this already passed the OTP, so an app that was installed
    /// before trusted devices existed enrols itself here instead of asking for another code.
    /// </summary>
    [HttpPost("trusted-devices")]
    public async Task<IActionResult> EnrollTrustedDevice(EnrollTrustedDeviceRequest request, CancellationToken ct)
    {
        var result = await _authService.EnrollTrustedDeviceAsync(this.GetUserId(), request.Label, ct);
        if (!result.Succeeded) return BadRequest(new { message = result.Error });
        Response.Headers.CacheControl = "no-store";
        return Ok(new { deviceToken = result.Value });
    }

    [HttpGet("trusted-devices")]
    public async Task<IActionResult> ListTrustedDevices(CancellationToken ct) =>
        Ok(await _authService.ListTrustedDevicesAsync(this.GetUserId(), ct));

    /// <summary>Stops trusting every phone: each one needs the OTP at its next sign-in.</summary>
    [HttpDelete("trusted-devices")]
    public async Task<IActionResult> RevokeAllTrustedDevices(CancellationToken ct) =>
        Ok(new { revoked = await _authService.RevokeTrustedDevicesAsync(this.GetUserId(), null, ct) });

    [HttpDelete("trusted-devices/{id:int}")]
    public async Task<IActionResult> RevokeTrustedDevice(int id, CancellationToken ct)
    {
        var count = await _authService.RevokeTrustedDevicesAsync(this.GetUserId(), id, ct);
        return count > 0 ? Ok(new { revoked = count }) : NotFound(new { message = "Trusted device not found." });
    }

    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken([FromBody] RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        await _deviceTokenValidator.ValidateAndThrowAsync(request, ct);
        var result = await _guardianService.RegisterDeviceTokenAsync(this.GetUserId(), request.FcmToken, ct);
        return result.Succeeded ? Ok(new { message = "Device registered." }) : BadRequest(new { message = result.Error });
    }

    [HttpPost("face-registration")]
    public async Task<IActionResult> RegisterFace(RegisterFaceRequest request, CancellationToken ct)
    {
        await _registerFaceValidator.ValidateAndThrowAsync(request, ct);
        var result = await _verificationService.RegisterFaceAsync(this.GetUserId(), request, ct);
        return result.Succeeded ? Ok(new { message = "Face registered." }) : BadRequest(new { message = result.Error });
    }

    // ---- Reference faces ----
    // The guardian's own reference faces only: the id always comes from the token, never the request.

    [HttpGet("face-references")]
    public async Task<IActionResult> GetMyFaceReferences(CancellationToken ct)
    {
        var result = await _faceReferences.ListAsync(this.GetUserId(), ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    /// <summary>Checks a photo before it is added, so the app can say "too blurry" or "more than one face" up front.</summary>
    [HttpPost("face-references/analyze")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> AnalyzeFaceImage(AnalyzeImageRequest request, CancellationToken ct)
    {
        await _analyzeValidator.ValidateAndThrowAsync(request, ct);
        var result = await _faceAnalysis.AnalyzeAsync(request, FaceActor.Guardian(this.GetUserId()), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpPost("face-references")]
    [EnableRateLimiting(RateLimitPolicies.FaceAnalyze)]
    [RequestSizeLimit(FaceUploads.SingleImageRequestLimit)]
    public async Task<IActionResult> AddMyFaceReference(AddFaceReferenceRequest request, CancellationToken ct)
    {
        await _addReferenceValidator.ValidateAndThrowAsync(request, ct);
        var result = await _faceReferences.AddAsync(this.GetUserId(), request, FaceActor.Guardian(this.GetUserId()), ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("face-references/{referenceId:int}")]
    public async Task<IActionResult> RemoveMyFaceReference(int referenceId, CancellationToken ct)
    {
        var result = await _faceReferences.RemoveAsync(this.GetUserId(), referenceId, FaceActor.Guardian(this.GetUserId()), ct);
        return result.Succeeded ? Ok(new { message = "Reference photo removed." }) : BadRequest(new { message = result.Error });
    }

    // ---- Photos ----
    // A guardian may read and manage only their own photo, and may read only the
    // photos of children linked to them. Every route below re-checks that, so
    // knowing another id is never enough to see a child's face.

    [HttpGet("photo")]
    public async Task<IActionResult> GetMyPhoto(CancellationToken ct)
    {
        var result = await _photos.GetGuardianPhotoAsync(this.GetUserId(), ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });

        var photo = result.Value!;
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Data, photo.ContentType);
    }

    [HttpPost("photo")]
    [RequestSizeLimit(PhotoUploads.RequestSizeLimit)]
    public async Task<IActionResult> UploadMyPhoto(IFormFile? file, CancellationToken ct)
    {
        var read = await PhotoUploads.ReadAsync(file, ct);
        if (!read.Succeeded) return BadRequest(new { message = read.Error });

        var result = await _photos.SaveGuardianPhotoAsync(this.GetUserId(), read.Value!, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("photo")]
    public async Task<IActionResult> DeleteMyPhoto(CancellationToken ct)
    {
        var result = await _photos.RemoveGuardianPhotoAsync(this.GetUserId(), ct);
        return result.Succeeded ? Ok(new { message = "Photo removed." }) : NotFound(new { message = result.Error });
    }

    [HttpGet("students/{studentId:int}/photo")]
    public async Task<IActionResult> GetStudentPhoto(int studentId, CancellationToken ct)
    {
        if (!await OwnsStudentAsync(studentId, ct)) return Forbid();

        var result = await _photos.GetStudentPhotoAsync(studentId, ct);
        if (!result.Succeeded) return NotFound(new { message = result.Error });

        var photo = result.Value!;
        Response.Headers.CacheControl = "private, max-age=60";
        return File(photo.Data, photo.ContentType);
    }

    private async Task<bool> OwnsStudentAsync(int studentId, CancellationToken ct)
    {
        var profile = await _guardianService.GetByIdAsync(this.GetUserId(), ct);
        return profile.Succeeded && profile.Value!.Students.Any(s => s.StudentId == studentId);
    }
}
