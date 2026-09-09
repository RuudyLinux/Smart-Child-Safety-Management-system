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
    Task<Result<GuardianAuthResultDto>> VerifyGuardianOtpAsync(VerifyGuardianOtpRequest request, CancellationToken ct = default);

    Task<Result<GuardianOtpChallengeDto>> ResendGuardianOtpAsync(ResendGuardianOtpRequest request, CancellationToken ct = default);

    /// <summary>Completes the mandatory first-login password reset against the ticket from step 2.</summary>
    Task<Result> ResetGuardianPasswordAsync(GuardianResetPasswordRequest request, CancellationToken ct = default);

    Task<Result> RegisterGuardianAsync(RegisterGuardianRequest request, CancellationToken ct = default);
    Task<Result> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(int userId, AppRole role, ChangePasswordRequest request, CancellationToken ct = default);
}
