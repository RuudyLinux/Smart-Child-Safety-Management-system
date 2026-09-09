namespace ChildGuard.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string Role, int UserId, string FullName, string Email);

public record RegisterGuardianRequest(string FullName, string Email, string Phone, string Password);

public record ForgotPasswordRequest(string Email, string Role);

public record ResetPasswordRequest(string Email, string Role, string ResetToken, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ---- Guardian OTP login flow ----

/// <summary>Guardians sign in with the contact number registered against their account, not their email.</summary>
public record GuardianLoginRequest(string ContactNumber, string Password);

public record VerifyGuardianOtpRequest(Guid ChallengeToken, string Code);

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

/// <summary>Handle for an open OTP challenge. Carries no code and no account details.</summary>
public record GuardianOtpChallengeDto(
    Guid ChallengeToken,
    DateTime ExpiresAtUtc,
    int CodeLength,
    int ResendAvailableInSeconds,
    int ResendsRemaining)
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
    DateTime? ResetTicketExpiresAtUtc);
