using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public interface IPasswordHasherService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(int userId, string email, string fullName, AppRole role);
}

/// <summary>
/// Sends the password-reset token to the user. No SMTP/email provider is in the
/// approved tech stack, so the dev implementation logs the token instead of
/// inventing an email service outside scope. Swappable later behind this interface.
/// </summary>
public interface IPasswordResetNotifier
{
    Task SendResetTokenAsync(string email, string resetToken, CancellationToken ct = default);
}

/// <summary>
/// Delivers a login OTP to the Guardian. No SMS gateway is part of the approved
/// stack, so the code goes to the Guardian's registered email address; the whole
/// Guardian is passed so an SMS implementation can use the contact number instead
/// without changing any caller. See README "OTP delivery".
/// </summary>
public interface IOtpSender
{
    /// <summary>Returns a failure when the code could not be delivered, so the caller can tell the guardian instead of leaving them waiting.</summary>
    Task<Common.Result> SendLoginOtpAsync(Domain.Entities.Guardian guardian, string code, TimeSpan validFor, CancellationToken ct = default);
}
