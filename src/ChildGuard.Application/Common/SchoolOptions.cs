using System.Globalization;

namespace ChildGuard.Application.Common;

/// <summary>
/// School-day settings bound from the "School" configuration section. These exist
/// so "late" and "absent" are decided by a stated policy rather than by a number
/// invented in the UI: a student is late when their recorded School Arrival is
/// after <see cref="LateArrivalAfter"/>, and absent when no arrival was recorded.
/// </summary>
public class SchoolOptions
{
    public string Name { get; set; } = "ChildGuard Academy";

    /// <summary>Local time-of-day after which a recorded arrival counts as late. 24-hour "HH:mm".</summary>
    public string LateArrivalAfter { get; set; } = "08:30";

    /// <summary>
    /// Local time-of-day after which a student with no recorded arrival is reported
    /// as a missing check-in on the alert centre. Before this, the day is simply
    /// still in progress and no alert is raised.
    /// </summary>
    public string MissingCheckInAfter { get; set; } = "09:30";

    /// <summary>Trailing window used for attendance statistics on a student profile.</summary>
    public int AttendanceWindowDays { get; set; } = 30;

    public TimeOnly LateAfterTime => Parse(LateArrivalAfter, new TimeOnly(8, 30));
    public TimeOnly MissingCheckInAfterTime => Parse(MissingCheckInAfter, new TimeOnly(9, 30));

    private static TimeOnly Parse(string value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
}
