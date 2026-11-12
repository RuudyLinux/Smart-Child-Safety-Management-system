using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

// Read models for the Guardian app (api/me). Every one is scoped to the signed-in guardian's
// own linked children; none carries another guardian's email, phone or biometric data.

/// <summary>Home screen: today's picture across all of the guardian's children.</summary>
public record GuardianDashboardDto(
    string GuardianName,
    DateOnly Today,
    int ChildrenCount,
    int PresentToday,
    int LateToday,
    int AtSchool,
    int OnBus,
    int LeftSchoolOrHome,
    int UnreadNotifications,
    int PendingPickups,
    IReadOnlyList<GuardianChildCardDto> Children,
    IReadOnlyList<JourneyEventDto> RecentEvents);

/// <summary>One child on the dashboard: identity plus the live safety snapshot.</summary>
public record GuardianChildCardDto(
    int StudentId,
    string FullName,
    string? Grade,
    string? Relationship,
    bool IsAuthorizedForPickup,
    bool HasPhoto,
    StudentSafetyStatus SafetyStatus,
    JourneyEventType? LastEventType,
    DateTime? LastEventAt,
    TimeOnly? CheckInTime,
    bool PresentToday,
    bool LateToday,
    string? BusNumber,
    string? RouteName);

/// <summary>Student details screen.</summary>
public record GuardianStudentDetailDto(
    GuardianChildCardDto Child,
    DateTime DateOfBirth,
    RecordStatus Status,
    bool HasRfidCard,
    AttendanceStatsDto AttendanceStats,
    GuardianTransportDto? Transport,
    IReadOnlyList<GuardianCoGuardianDto> Guardians,
    IReadOnlyList<JourneyEventDto> TodaysEvents);

/// <summary>Assigned bus, route and driver. Null on the detail when the child has no active assignment.</summary>
public record GuardianTransportDto(
    string BusNumber,
    int Capacity,
    string? RouteName,
    string? RouteDescription,
    string? DriverName,
    string? DriverPhone,
    DateTime AssignedAt);

/// <summary>Another guardian linked to the same child. Name and relationship only — never contact details.</summary>
public record GuardianCoGuardianDto(
    int GuardianId,
    string FullName,
    string? Relationship,
    bool IsAuthorizedForPickup,
    bool IsYou);

/// <summary>Attendance and journey summary for one child over a date range (school calendar days).</summary>
public record GuardianStudentReportDto(
    int StudentId,
    string StudentName,
    DateOnly From,
    DateOnly To,
    int SchoolDaysObserved,
    int DaysPresent,
    int DaysLate,
    double PresentRate,
    int BusBoardings,
    int BusDropoffs,
    int PickupsApproved,
    IReadOnlyList<GuardianReportDayDto> Days);

/// <summary>One school day in a report. Times are school-local.</summary>
public record GuardianReportDayDto(
    DateOnly Date,
    TimeOnly? CheckIn,
    TimeOnly? CheckOut,
    bool Late,
    DateTime? BusBoardingAt,
    DateTime? BusDropoffAt);

/// <summary>
/// A pickup attempt for one of the guardian's children. <c>Kind</c> is "Guardian" for a face-verified
/// pickup by a permanent guardian, "Temporary" for an authorised temporary pickup person.
/// </summary>
public record GuardianPickupDto(
    string Kind,
    int Id,
    int StudentId,
    string StudentName,
    string PickedUpBy,
    bool IsYou,
    DateTime AttemptedAt,
    PickupVerificationState State,
    DateTime? ExpiresAt);
