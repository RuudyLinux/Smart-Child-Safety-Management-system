using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface INotificationService
{
    Task<List<NotificationDto>> GetForGuardianAsync(int guardianId, CancellationToken ct = default);
    Task<Result> MarkReadAsync(int notificationId, int requestingGuardianId, CancellationToken ct = default);
}
