namespace ChildGuard.Application.DTOs;

public record DashboardSummaryDto(
    int TotalStudents, int ActiveRfidCards, int TotalGuardians, int ActiveBuses,
    int TodaysAttendanceCount, int TodaysJourneyEventCount, int TodaysVerificationCount,
    IReadOnlyList<JourneyEventDto> RecentActivity);
