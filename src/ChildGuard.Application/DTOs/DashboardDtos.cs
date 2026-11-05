using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record DashboardSummaryDto(
    int TotalStudents, int ActiveRfidCards, int TotalGuardians, int ActiveBuses,
    int TodaysAttendanceCount, int TodaysJourneyEventCount, int TodaysVerificationCount,
    VerificationStatusDto TodaysVerificationStatus,
    IReadOnlyList<JourneyEventDto> RecentActivity,
    AttendanceOverviewDto Attendance,
    JourneyOverviewDto Journey,
    TransportOverviewDto Transport,
    int PendingVerifications,
    IReadOnlyList<AttendanceTrendPointDto> AttendanceTrend,
    IReadOnlyList<SafetyAlertDto> Alerts,
    IReadOnlyList<GradeAttendanceDto> AttendanceByGrade);

/// <summary>Breakdown behind "Today's Verifications" so the dashboard shows outcome, not just volume.</summary>
public record VerificationStatusDto(int Matched, int NoMatch, int PickupsApproved, int PickupsRejected);

/// <summary>
/// Today's attendance against the active roster. "Late" is an arrival recorded after
/// the configured school start time (School:LateArrivalAfter) — a real comparison
/// against recorded arrival times, not an estimate.
/// </summary>
public record AttendanceOverviewDto(
    int RosterSize, int Present, int Late, int Absent,
    double PresentRate, double? PreviousDayPresentRate, double? RateChange, string LateAfter);

/// <summary>Where the active roster is right now, counted from each student's latest journey event.</summary>
public record JourneyOverviewDto(
    int OnBusToSchool, int AtSchool, int LeftSchool, int DroppedOffHome, int NoActivityToday);

public record TransportOverviewDto(
    int ActiveBuses, int InactiveBuses, int Drivers, int Routes,
    int StudentsAssigned, int StudentsUnassigned, int ScannersConnected, int ScannersTotal);

public record AttendanceTrendPointDto(DateOnly Date, int Present, int RosterSize, double PresentRate);

public record GradeAttendanceDto(string Grade, int RosterSize, int Present, double PresentRate);

/// <summary>
/// An operational condition an admin should act on. Every alert is derived from
/// recorded data — a failed verification, a missing check-in, a disconnected
/// scanner — so the centre never shows an invented incident.
/// </summary>
public record SafetyAlertDto(
    string Key, AlertSeverity Severity, string Category, string Title, string Detail,
    string RecommendedAction, DateTime? OccurredAt, int? StudentId, string? StudentName, string? Link);
