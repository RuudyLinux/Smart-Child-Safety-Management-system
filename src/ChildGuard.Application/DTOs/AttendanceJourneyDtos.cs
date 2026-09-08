using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record AttendanceRecordDto(int AttendanceId, int StudentId, string StudentName, AttendanceEventType EventType, DateOnly Date, TimeOnly Time, RecordStatus Status);

public record JourneyEventDto(int JourneyEventId, int StudentId, string StudentName, JourneyEventType EventType, DateTime Timestamp, EventSource Source);

public record NotificationDto(int NotificationId, string Title, string Message, NotificationType Type, bool IsRead, DateTime SentAt, int? StudentId);
