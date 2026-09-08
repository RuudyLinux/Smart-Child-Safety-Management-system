using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Notification
{
    public int NotificationId { get; set; }
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public string? RelatedEntityType { get; set; }
    public int? RelatedEntityId { get; set; }
    public bool IsRead { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public string? FcmStatus { get; set; }
}
