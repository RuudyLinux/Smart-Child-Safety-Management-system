using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Identity;

/// <summary>
/// Dev-only notifier: logs the reset token instead of emailing it, because no
/// email/SMTP provider is part of the approved stack (Auth/notifications spec
/// only names Firebase FCM, which is push-only). Swap this implementation
/// behind IPasswordResetNotifier once a real mail provider is chosen.
/// </summary>
public class LoggingPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly ILogger<LoggingPasswordResetNotifier> _logger;
    public LoggingPasswordResetNotifier(ILogger<LoggingPasswordResetNotifier> logger) => _logger = logger;

    public Task SendResetTokenAsync(string email, string resetToken, CancellationToken ct = default)
    {
        _logger.LogWarning("Password reset requested for {Email}. Token (dev-only log): {Token}", email, resetToken);
        return Task.CompletedTask;
    }
}
