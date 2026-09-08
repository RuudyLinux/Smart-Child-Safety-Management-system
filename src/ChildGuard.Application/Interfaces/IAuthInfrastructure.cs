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
