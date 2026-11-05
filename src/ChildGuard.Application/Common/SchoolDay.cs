namespace ChildGuard.Application.Common;

/// <summary>
/// The school-day boundary used by the journey state machine. Matches the live
/// board (SafetyReadService), which already treats only today's events as the
/// student's current position: both use the UTC calendar day, the same day that
/// Attendance.Date is stamped with.
/// </summary>
public static class SchoolDay
{
    public static DateTime StartUtc(DateTime nowUtc) => nowUtc.Date;
}
