using ChildGuard.Application.Interfaces;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;

namespace ChildGuard.Infrastructure.Notifications;

/// <summary>
/// Real Firebase Cloud Messaging sender. Only registered in DI when a service-
/// account credential is configured (see DependencyInjection.AddInfrastructure) —
/// otherwise LoggingPushNotificationSender is used instead. No credential is
/// ever hardcoded; FirebaseApp is initialized from the path in configuration.
/// </summary>
public class FcmPushNotificationSender : IPushNotificationSender
{
    private readonly IGuardianRepository _guardians;
    private readonly ILogger<FcmPushNotificationSender> _logger;

    public FcmPushNotificationSender(IGuardianRepository guardians, ILogger<FcmPushNotificationSender> logger)
    {
        _guardians = guardians;
        _logger = logger;
    }

    public async Task SendAsync(string title, string message, int guardianId, CancellationToken ct = default)
    {
        var token = await _guardians.GetFcmTokenAsync(guardianId, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("Guardian {GuardianId} has no registered device token — push skipped.", guardianId);
            return;
        }

        try
        {
            // Message.Token is flagged obsolete in favor of Firebase Installation IDs (Fid),
            // but registration-token push (what the Flutter app registers via firebase_messaging)
            // is still the standard, supported delivery mechanism — Fid is a different targeting
            // concept, not a drop-in replacement here.
#pragma warning disable CS0618
            await FirebaseMessaging.DefaultInstance.SendAsync(new Message
            {
                Token = token,
                Notification = new FirebaseAdmin.Messaging.Notification { Title = title, Body = message }
            }, ct);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            // A push failure (expired token, misconfigured project, offline device) must
            // never break the caller's flow — the in-app notification row is already saved.
            _logger.LogWarning(ex, "FCM push failed for guardian {GuardianId}.", guardianId);
        }
    }
}
