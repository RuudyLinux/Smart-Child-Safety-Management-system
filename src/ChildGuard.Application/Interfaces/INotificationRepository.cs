using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken ct = default);
    Task<List<Notification>> GetForGuardianAsync(int guardianId, CancellationToken ct = default);
    Task<Notification?> GetByIdAsync(int id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Push delivery abstraction. Real implementation talks to Firebase Cloud Messaging
/// (wired in the Notifications phase); until then a no-op/logging implementation
/// keeps in-app notification history working without a push credential configured.
/// </summary>
public interface IPushNotificationSender
{
    Task SendAsync(string title, string message, int guardianId, CancellationToken ct = default);
}
