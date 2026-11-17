using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Notifications;

/// <summary>
/// Fallback used when no email provider key is configured, so the app still runs
/// end to end in development. Logs the plain-text body — which can contain an
/// initial password or an OTP — so it is only ever registered when the provider
/// is absent, exactly like LoggingPushNotificationSender.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Email provider not configured. Would send \"{Subject}\" to {Recipient} (dev-only log):\n{Body}",
            message.Subject, message.ToEmail, message.TextBody);
        return Task.FromResult(Result.Success());
    }
}
