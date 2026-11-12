using ChildGuard.Api.Security;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<GuardianLoginRequest> _guardianLoginValidator;
    private readonly IValidator<VerifyGuardianOtpRequest> _verifyOtpValidator;
    private readonly IValidator<ResendGuardianOtpRequest> _resendOtpValidator;
    private readonly IValidator<GuardianResetPasswordRequest> _guardianResetValidator;
    private readonly IValidator<RegisterGuardianRequest> _registerValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public AuthController(
        IAuthService authService,
        IValidator<LoginRequest> loginValidator,
        IValidator<GuardianLoginRequest> guardianLoginValidator,
        IValidator<VerifyGuardianOtpRequest> verifyOtpValidator,
        IValidator<ResendGuardianOtpRequest> resendOtpValidator,
        IValidator<GuardianResetPasswordRequest> guardianResetValidator,
        IValidator<RegisterGuardianRequest> registerValidator,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _authService = authService;
        _loginValidator = loginValidator;
        _guardianLoginValidator = guardianLoginValidator;
        _verifyOtpValidator = verifyOtpValidator;
        _resendOtpValidator = resendOtpValidator;
        _guardianResetValidator = guardianResetValidator;
        _registerValidator = registerValidator;
        _forgotValidator = forgotValidator;
        _resetValidator = resetValidator;
        _changePasswordValidator = changePasswordValidator;
    }

    [HttpPost("admin/login")]
    public async Task<IActionResult> AdminLogin(LoginRequest request, CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.LoginAdminAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { message = result.Error });
    }

    /// <summary>
    /// Step 1 of Guardian sign-in. Validates the contact number and password and
    /// sends an OTP; returns a challenge handle, never a token.
    /// </summary>
    [HttpPost("guardian/login")]
    public async Task<IActionResult> GuardianLogin(GuardianLoginRequest request, CancellationToken ct)
    {
        await _guardianLoginValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.SignInGuardianAsync(request, ct);
        // An OTP challenge keeps its original JSON shape; a trusted phone gets the verify-otp shape instead
        // (the "stage" field tells the app which one it received).
        if (result.Succeeded) return Ok((object?)result.Value!.Challenge ?? result.Value.Auth);
        // A code that could not be emailed is a service problem, not bad credentials.
        return result.Error == AuthService.OtpDeliveryFailedMessage
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.Error })
            : Unauthorized(new { message = result.Error });
    }

    /// <summary>
    /// Step 2. On success returns either a session (returning Guardian) or a
    /// password-reset ticket (first login on the default password).
    /// </summary>
    [HttpPost("guardian/verify-otp")]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> GuardianVerifyOtp(VerifyGuardianOtpRequest request, CancellationToken ct)
    {
        await _verifyOtpValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.VerifyGuardianOtpAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { message = result.Error });
    }

    [HttpPost("guardian/resend-otp")]
    [EnableRateLimiting(RateLimitPolicies.Otp)]
    public async Task<IActionResult> GuardianResendOtp(ResendGuardianOtpRequest request, CancellationToken ct)
    {
        await _resendOtpValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.ResendGuardianOtpAsync(request, ct);
        if (result.Succeeded) return Ok(result.Value);
        return result.Error == AuthService.OtpDeliveryFailedMessage
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.Error })
            : BadRequest(new { message = result.Error });
    }

    /// <summary>Completes the mandatory first-login password reset using the ticket from verify-otp.</summary>
    [HttpPost("guardian/reset-password")]
    public async Task<IActionResult> GuardianResetPassword(GuardianResetPasswordRequest request, CancellationToken ct)
    {
        await _guardianResetValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.ResetGuardianPasswordAsync(request, ct);
        return result.Succeeded
            ? Ok(new { message = "Password updated. Please sign in with your new password." })
            : BadRequest(new { message = result.Error });
    }

    [HttpPost("guardian/register")]
    public async Task<IActionResult> GuardianRegister(RegisterGuardianRequest request, CancellationToken ct)
    {
        await _registerValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.RegisterGuardianAsync(request, ct);
        return result.Succeeded ? Ok(new { message = "Registration successful." }) : Conflict(new { message = result.Error });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await _forgotValidator.ValidateAndThrowAsync(request, ct);
        await _authService.ForgotPasswordAsync(request, ct);
        // Always return generic success — do not reveal whether the account exists.
        return Ok(new { message = "If the account exists, a reset token has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _resetValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.ResetPasswordAsync(request, ct);
        return result.Succeeded ? Ok(new { message = "Password reset successful." }) : BadRequest(new { message = result.Error });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _changePasswordValidator.ValidateAndThrowAsync(request, ct);
        var role = User.IsInRole(nameof(AppRole.Admin)) ? AppRole.Admin : AppRole.Guardian;
        var result = await _authService.ChangePasswordAsync(this.GetUserId(), role, request, ct);
        return result.Succeeded ? Ok(new { message = "Password changed." }) : BadRequest(new { message = result.Error });
    }
}
