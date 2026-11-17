using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// The school's regular week: which weekdays are school days and the school-local
/// times every regular day runs to. A single row (id 1). Until an admin saves one,
/// the "School" configuration section supplies the same values, so a deployment that
/// never opens the School Time page behaves exactly as before.
/// </summary>
public class SchoolTimingSettings
{
    public const int SingletonId = 1;

    public int SchoolTimingSettingsId { get; set; } = SingletonId;

    public SchoolWeekdays WorkingDays { get; set; } = SchoolWeekdays.MondayToFriday;

    /// <summary>School-local time the school day starts (gates open, first period).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>School-local time after which a recorded arrival counts as late.</summary>
    public TimeOnly LateArrivalAfter { get; set; }

    /// <summary>School-local time after which a student with no arrival raises a missing check-in alert.</summary>
    public TimeOnly MissingCheckInAfter { get; set; }

    /// <summary>School-local time the school day ends.</summary>
    public TimeOnly DismissalTime { get; set; }

    /// <summary>Minutes after dismissal before a student still at school raises a not-picked-up alert.</summary>
    public int PickupGraceMinutes { get; set; }

    /// <summary>Trailing window used for attendance statistics on a student profile.</summary>
    public int AttendanceWindowDays { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByAdminId { get; set; }
}

/// <summary>
/// One school-local date that differs from the regular week: a holiday, a half day,
/// a day with different timings, or a normally-closed weekday the school opens on.
/// One row per date; removing an exception sets <see cref="Status"/> to Inactive and
/// re-adding the date reuses the row.
/// </summary>
public class SchoolCalendarDay
{
    public int SchoolCalendarDayId { get; set; }

    /// <summary>School-local calendar date (School:TimeZone).</summary>
    public DateOnly Date { get; set; }

    public SchoolDayType DayType { get; set; }

    /// <summary>Shown to admins and guardians, e.g. "Diwali" or "Annual sports day".</summary>
    public string Title { get; set; } = default!;

    /// <summary>Overrides the regular start time for this date (not used for holidays).</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Overrides the regular late cutoff for this date (not used for holidays).</summary>
    public TimeOnly? LateArrivalAfter { get; set; }

    /// <summary>Overrides the regular dismissal time for this date (required for a half day).</summary>
    public TimeOnly? DismissalTime { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
