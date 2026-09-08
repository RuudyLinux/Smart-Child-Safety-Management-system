using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChildGuard.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RegisterGuardianRequest> _registerValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotValidator;
    private readonly IValidator<ResetPasswordRequest> _resetValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public AuthController(
        IAuthService authService,
        IValidator<LoginRequest> loginValidator,
        IValidator<RegisterGuardianRequest> registerValidator,
        IValidator<ForgotPasswordRequest> forgotValidator,
        IValidator<ResetPasswordRequest> resetValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _authService = authService;
        _loginValidator = loginValidator;
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

    [HttpPost("guardian/login")]
    public async Task<IActionResult> GuardianLogin(LoginRequest request, CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);
        var result = await _authService.LoginGuardianAsync(request, ct);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { message = result.Error });
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
