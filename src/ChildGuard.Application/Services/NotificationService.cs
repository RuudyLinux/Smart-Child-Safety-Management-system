using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    public NotificationService(INotificationRepository repo) => _repo = repo;

    public async Task<List<NotificationDto>> GetForGuardianAsync(int guardianId, CancellationToken ct = default) =>
        (await _repo.GetForGuardianAsync(guardianId, ct))
            .Select(n => new NotificationDto(n.NotificationId, n.Title, n.Message, n.Type, n.IsRead, n.SentAt, n.StudentId))
            .ToList();

    public async Task<Result> MarkReadAsync(int notificationId, int requestingGuardianId, CancellationToken ct = default)
    {
        var notification = await _repo.GetByIdAsync(notificationId, ct);
        if (notification is null || notification.GuardianId != requestingGuardianId)
            return Result.Failure("Notification not found.");

        notification.IsRead = true;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }
}
