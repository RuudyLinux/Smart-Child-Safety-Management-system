using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record AttendanceRecordDto(
    int AttendanceId, int StudentId, string StudentName, string? Grade,
    AttendanceEventType EventType, DateOnly Date, TimeOnly Time, RecordStatus Status, bool IsLate);

public record JourneyEventDto(int JourneyEventId, int StudentId, string StudentName, JourneyEventType EventType, DateTime Timestamp, EventSource Source);

/// <summary>
/// One page of the journey report. <see cref="TotalCount"/> is the number of events
/// in the whole [From, To] range, so a caller can always tell when <see cref="Items"/>
/// is only part of the result. From/To are UTC calendar dates, inclusive.
/// </summary>
public record JourneyReportPageDto(
    IReadOnlyList<JourneyEventDto> Items, int TotalCount, int Page, int PageSize,
    DateOnly From, DateOnly To, int? StudentId)
{
    public int TotalPages => TotalCount == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasMore => Page < TotalPages;
}

public record NotificationDto(int NotificationId, string Title, string Message, NotificationType Type, bool IsRead, DateTime SentAt, int? StudentId);

/// <summary>
/// One student's current position in the journey cycle, for the live Journey
/// Tracking board. Derived entirely from recorded checkpoint events — this system
/// records checkpoints, not geographic location, so no coordinates exist to report.
/// </summary>
public record StudentJourneyStatusDto(
    int StudentId, string StudentName, string? Grade, bool HasPhoto,
    StudentSafetyStatus SafetyStatus, JourneyEventType? LastEventType, DateTime? LastEventAt,
    string? BusNumber, string? RouteName, IReadOnlyList<JourneyEventDto> TodaysEvents);
