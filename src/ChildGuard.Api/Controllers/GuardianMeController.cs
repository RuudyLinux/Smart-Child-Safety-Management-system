using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

/// <summary>Self-service endpoints for the authenticated Guardian — backs the Flutter app.</summary>
[ApiController]
[Route("api/me")]
[Authorize(Policy = "GuardianOnly")]
public class GuardianMeController : ControllerBase
{
    private readonly IGuardianService _guardianService;
    private readonly IAuthService _authService;
    private readonly IAttendanceJourneyService _attendanceJourney;
    private readonly INotificationService _notificationService;
    private readonly IVerificationService _verificationService;
    private readonly IValidator<UpdateProfileRequest> _profileValidator;
    private readonly IValidator<RegisterDeviceTokenRequest> _deviceTokenValidator;
    private readonly IValidator<RegisterFaceRequest> _registerFaceValidator;

    public GuardianMeController(
        IGuardianService guardianService,
        IAuthService authService,
        IAttendanceJourneyService attendanceJourney,
        INotificationService notificationService,
        IVerificationService verificationService,
        IValidator<UpdateProfileRequest> profileValidator,
        IValidator<RegisterDeviceTokenRequest> deviceTokenValidator,
        IValidator<RegisterFaceRequest> registerFaceValidator)
    {
        _guardianService = guardianService;
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
    public async Task<IActionResult> GetStudentAttendance(int studentId, CancellationToken ct)
    {
        if (!await OwnsStudentAsync(studentId, ct)) return Forbid();
        return Ok(await _attendanceJourney.GetAttendanceAsync(studentId, null, null, ct));
    }

    [HttpGet("students/{studentId:int}/journey")]
    public async Task<IActionResult> GetStudentJourney(int studentId, CancellationToken ct)
    {
        if (!await OwnsStudentAsync(studentId, ct)) return Forbid();
        return Ok(await _attendanceJourney.GetJourneyHistoryAsync(studentId, ct));
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

    private async Task<bool> OwnsStudentAsync(int studentId, CancellationToken ct)
    {
        var profile = await _guardianService.GetByIdAsync(this.GetUserId(), ct);
        return profile.Succeeded && profile.Value!.Students.Any(s => s.StudentId == studentId);
    }
}
