using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record StudentListItemDto(
    int StudentId, string FullName, string? Grade, RecordStatus Status,
    string? MaskedRfid, int GuardianCount,
    bool HasPhoto, string? PrimaryGuardianName,
    StudentSafetyStatus SafetyStatus, bool PresentToday, TimeOnly? CheckInTime,
    DateTime? LastEventAt, JourneyEventType? LastEventType, string? BusNumber);

public record StudentDetailDto(
    int StudentId, string FullName, DateTime DateOfBirth, string? Grade, RecordStatus Status,
    string? MaskedRfid, IReadOnlyList<GuardianSummaryDto> Guardians, DateTime CreatedAt,
    bool HasPhoto, DateTime? PhotoUpdatedAt,
    StudentSafetyStatus SafetyStatus, bool PresentToday, TimeOnly? CheckInTime,
    DateTime? LastEventAt, JourneyEventType? LastEventType,
    string? BusNumber, string? RouteName, string? DriverName,
    RfidCardStatus? RfidStatus, DateTime? RfidAssignedAt,
    AttendanceStatsDto AttendanceStats,
    IReadOnlyList<JourneyEventDto> RecentJourney,
    IReadOnlyList<AttendanceRecordDto> RecentAttendance);

/// <summary>Attendance counted over the trailing window shown on a student profile.</summary>
public record AttendanceStatsDto(int WindowDays, int DaysPresent, int DaysLate, int SchoolDaysObserved, double PresentRate);

public record GuardianSummaryDto(
    int GuardianId, string FullName, string Email, string? Phone, string? Relationship,
    bool IsAuthorizedForPickup, bool HasPhoto, bool FaceRegistered);

public record CreateStudentRequest(string FullName, DateTime DateOfBirth, string? Grade);

public record UpdateStudentRequest(string FullName, DateTime DateOfBirth, string? Grade);

public record UpdateStudentStatusRequest(RecordStatus Status);

public record AssignGuardianRequest(int GuardianId, string? Relationship, bool IsAuthorizedForPickup);

/// <summary>Edit of an existing student-guardian link. The pair itself is identified by the route.</summary>
public record UpdateStudentGuardianRequest(string? Relationship, bool IsAuthorizedForPickup);
