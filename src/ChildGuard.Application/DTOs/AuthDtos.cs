namespace ChildGuard.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Role, int UserId, string FullName, string Email);

public record RegisterGuardianRequest(string FullName, string Email, string Phone, string Password);

public record ForgotPasswordRequest(string Email, string Role);

public record ResetPasswordRequest(string Email, string Role, string ResetToken, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ---- Guardian OTP login flow ----

/// <summary>Guardians sign in with the contact number registered against their account, not their email.</summary>
/// <param name="DeviceToken">Secret the app received when this phone was trusted. Optional; without it (or when it is
/// unknown, revoked or lapsed) sign-in needs the emailed OTP as before.</param>
public record GuardianLoginRequest(string ContactNumber, string Password, string? DeviceToken = null);

/// <param name="TrustDevice">When true, a successful verification also trusts this phone, so the next sign-in from it
/// needs only the password. The secret comes back once, in <see cref="GuardianAuthResultDto.DeviceToken"/>.</param>
public record VerifyGuardianOtpRequest(Guid ChallengeToken, string Code, bool TrustDevice = false, string? DeviceLabel = null);

public record ResendGuardianOtpRequest(Guid ChallengeToken);

/// <summary>Mandatory first-login password reset. Authorised by the ticket issued after OTP verification, never by a client-side flag.</summary>
public record GuardianResetPasswordRequest(string ResetTicket, string NewPassword, string ConfirmPassword);

/// <summary>What the client must do next. The server decides this; the client only renders it.</summary>
public enum GuardianAuthStage
{
    OtpRequired = 1,
    PasswordResetRequired = 2,
    Authenticated = 3
}

/// <summary>Handle for an open OTP challenge. Carries no code. <c>MaskedEmail</c> (for example a***@gmail.com) says which inbox the code was sent to.</summary>
public record GuardianOtpChallengeDto(
    Guid ChallengeToken,
    DateTime ExpiresAtUtc,
    int CodeLength,
    int ResendAvailableInSeconds,
    int ResendsRemaining,
    string? MaskedEmail = null)
{
    public GuardianAuthStage Stage => GuardianAuthStage.OtpRequired;
}

/// <summary>
/// Outcome of OTP verification: either a session (returning Guardian) or a
/// short-lived reset ticket (first login on the default password). Never both.
/// </summary>
public record GuardianAuthResultDto(
    GuardianAuthStage Stage,
    LoginResponse? Session,
    string? ResetTicket,
    DateTime? ResetTicketExpiresAtUtc,
    string? DeviceToken = null);

/// <summary>Result of the password step: an OTP challenge, or (trusted phone) the same outcome verify-otp gives.</summary>
public record GuardianSignInDto(GuardianOtpChallengeDto? Challenge, GuardianAuthResultDto? Auth);

public record EnrollTrustedDeviceRequest(string? Label);

/// <summary>A trusted phone as listed to its guardian. The secret and its hash are never returned.</summary>
public record TrustedDeviceDto(int Id, string? Label, DateTime CreatedAt, DateTime LastUsedAt, DateTime ExpiresAt);
