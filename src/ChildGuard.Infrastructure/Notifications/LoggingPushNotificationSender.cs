using ChildGuard.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Notifications;

/// <summary>
/// Dev/placeholder push sender — logs instead of calling Firebase. Real FCM
/// integration is wired in the Notifications phase once a service-account
/// credential is supplied via configuration (never hardcoded).
/// </summary>
public class LoggingPushNotificationSender : IPushNotificationSender
{
    private readonly ILogger<LoggingPushNotificationSender> _logger;
    public LoggingPushNotificationSender(ILogger<LoggingPushNotificationSender> logger) => _logger = logger;

    public Task SendAsync(string title, string message, int guardianId, CancellationToken ct = default)
    {
        _logger.LogInformation("[PUSH-DEV] Guardian {GuardianId}: {Title} — {Message}", guardianId, title, message);
        return Task.CompletedTask;
    }
}
