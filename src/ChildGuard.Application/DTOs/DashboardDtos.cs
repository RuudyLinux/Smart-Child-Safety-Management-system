namespace ChildGuard.Application.DTOs;

public record DashboardSummaryDto(
    int TotalStudents, int ActiveRfidCards, int TotalGuardians, int ActiveBuses,
    int TodaysAttendanceCount, int TodaysJourneyEventCount, int TodaysVerificationCount,
    VerificationStatusDto TodaysVerificationStatus,
    IReadOnlyList<JourneyEventDto> RecentActivity);

/// <summary>Breakdown behind "Today's Verifications" so the dashboard shows outcome, not just volume.</summary>
public record VerificationStatusDto(int Matched, int NoMatch, int PickupsApproved, int PickupsRejected);
