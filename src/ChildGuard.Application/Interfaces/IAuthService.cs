using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAdminAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Step 1 of Guardian sign-in: validates the contact number and password and
    /// raises an OTP challenge. Deliberately returns no token — a Guardian is only
    /// authenticated once <see cref="VerifyGuardianOtpAsync"/> succeeds.
    /// </summary>
    Task<Result<GuardianOtpChallengeDto>> LoginGuardianAsync(GuardianLoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Step 2: verifies the code. Returns a session for a returning Guardian, or a
    /// password-reset ticket when the account is still on its default password.
    /// </summary>
    /// <summary>
    /// Password step with the trusted-device shortcut: a phone presenting a valid device token skips the OTP and
    /// gets the same outcome verify-otp would give; any other phone gets an OTP challenge exactly as
    /// <see cref="LoginGuardianAsync"/> does.
    /// </summary>
    Task<Result<GuardianSignInDto>> SignInGuardianAsync(GuardianLoginRequest request, CancellationToken ct = default);

    /// <summary>Trusts the phone of an already signed-in guardian. Returns the device secret, shown once.</summary>
    Task<Result<string>> EnrollTrustedDeviceAsync(int guardianId, string? label, CancellationToken ct = default);
    Task<List<TrustedDeviceDto>> ListTrustedDevicesAsync(int guardianId, CancellationToken ct = default);
    /// <summary>Revokes one trusted phone, or all of the guardian's when <paramref name="deviceId"/> is null.</summary>
    Task<int> RevokeTrustedDevicesAsync(int guardianId, int? deviceId, CancellationToken ct = default);

    Task<Result<GuardianAuthResultDto>> VerifyGuardianOtpAsync(VerifyGuardianOtpRequest request, CancellationToken ct = default);

    Task<Result<GuardianOtpChallengeDto>> ResendGuardianOtpAsync(ResendGuardianOtpRequest request, CancellationToken ct = default);

    /// <summary>Completes the mandatory first-login password reset against the ticket from step 2.</summary>
    Task<Result> ResetGuardianPasswordAsync(GuardianResetPasswordRequest request, CancellationToken ct = default);

    Task<Result> RegisterGuardianAsync(RegisterGuardianRequest request, CancellationToken ct = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int userId, AppRole role, ChangePasswordRequest request, CancellationToken ct = default);
}
