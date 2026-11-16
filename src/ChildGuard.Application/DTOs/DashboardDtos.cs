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
    IReadOnlyList<GradeAttendanceDto> AttendanceByGrade,
    AdminOverviewDto? Admin = null);

/// <summary>Record-keeping KPIs for the admin: people, RFID inventory, fleet, and the gaps that need follow-up.</summary>
public record AdminOverviewDto(PeopleOverviewDto People, RfidOverviewDto Rfid, FleetOverviewDto Fleet, CoverageGapsDto Gaps);

public record PeopleOverviewDto(
    int Students, int ActiveStudents, int InactiveStudents, int ArchivedStudents,
    int Guardians, int ActiveGuardians, int InactiveGuardians, int ArchivedGuardians);

/// <param name="Assigned">Cards currently held by a student.</param>
/// <param name="Available">Active spare cards not held by anyone, ready to assign.</param>
public record RfidOverviewDto(int Total, int Assigned, int Available, int Inactive, int Lost, int Damaged, int Retired);

public record FleetOverviewDto(
    int Drivers, int ActiveDrivers, int Buses, int BusesActive, int BusesUnderMaintenance, int BusesOutOfService, int BusesInactive,
    int Routes, int ActiveRoutes, int StudentsAssigned, IReadOnlyList<BusLoadDto> BusLoads);

public record BusLoadDto(int BusId, string BusNumber, BusStatus Status, int Capacity, int Students);

/// <summary>Active students missing something, and drivers whose licence needs attention.</summary>
public record CoverageGapsDto(
    int StudentsWithoutGuardian, int StudentsWithoutRfid, int StudentsWithoutTransport,
    int LicencesExpired, int LicencesExpiringSoon, int LicencesNotRecorded);

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
