using System.Globalization;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

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

    /// <summary>
    /// IANA (or Windows) id of the school's time zone. Every school-calendar rule —
    /// the school day boundary, Attendance.Date/Time, the late cutoff, report date
    /// ranges — is evaluated in this zone. Instants (timestamps) stay in UTC.
    /// </summary>
    public string TimeZone { get; set; } = "Asia/Kolkata";

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

    /// <summary>Default school-local start of the school day, 24-hour "HH:mm".</summary>
    public string StartTime { get; set; } = "08:00";

    /// <summary>Default school-local dismissal time, 24-hour "HH:mm".</summary>
    public string DismissalTime { get; set; } = "14:30";

    /// <summary>Default minutes after dismissal before a student still at school raises an alert.</summary>
    public int PickupGraceMinutes { get; set; } = 30;

    /// <summary>Default school weekdays, comma separated (e.g. "Monday,Tuesday,Wednesday,Thursday,Friday").</summary>
    public string WorkingDays { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday";

    public TimeOnly LateAfterTime => Parse(LateArrivalAfter, new TimeOnly(8, 30));
    public TimeOnly MissingCheckInAfterTime => Parse(MissingCheckInAfter, new TimeOnly(9, 30));
    public TimeOnly StartTimeOfDay => Parse(StartTime, new TimeOnly(8, 0));
    public TimeOnly DismissalTimeOfDay => Parse(DismissalTime, new TimeOnly(14, 30));

    /// <summary>
    /// The regular week as configured here. Used until an admin saves the School Time
    /// settings, so a deployment that never does keeps its configured behaviour.
    /// </summary>
    public SchoolTimingSettings ToDefaultSettings() => new()
    {
        WorkingDays = ParseWorkingDays(WorkingDays),
        StartTime = StartTimeOfDay,
        LateArrivalAfter = LateAfterTime,
        MissingCheckInAfter = MissingCheckInAfterTime,
        DismissalTime = DismissalTimeOfDay,
        PickupGraceMinutes = Math.Clamp(PickupGraceMinutes, 0, 240),
        AttendanceWindowDays = Math.Max(1, AttendanceWindowDays)
    };

    private static SchoolWeekdays ParseWorkingDays(string value)
    {
        var days = SchoolWeekdays.None;
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<DayOfWeek>(part, ignoreCase: true, out var day))
                days |= SchoolWeek.Flag(day);
        return days == SchoolWeekdays.None ? SchoolWeekdays.MondayToFriday : days;
    }

    private static TimeOnly Parse(string value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
}
