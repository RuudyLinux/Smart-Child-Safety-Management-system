using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Common;

/// <summary>Maps between <see cref="DayOfWeek"/> and the stored <see cref="SchoolWeekdays"/> mask.</summary>
public static class SchoolWeek
{
    public static SchoolWeekdays Flag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => SchoolWeekdays.Monday,
        DayOfWeek.Tuesday => SchoolWeekdays.Tuesday,
        DayOfWeek.Wednesday => SchoolWeekdays.Wednesday,
        DayOfWeek.Thursday => SchoolWeekdays.Thursday,
        DayOfWeek.Friday => SchoolWeekdays.Friday,
        DayOfWeek.Saturday => SchoolWeekdays.Saturday,
        _ => SchoolWeekdays.Sunday
    };

    public static SchoolWeekdays FromDays(IEnumerable<DayOfWeek> days) =>
        days.Aggregate(SchoolWeekdays.None, (mask, d) => mask | Flag(d));

    /// <summary>Monday-first, the order a school week is read in.</summary>
    public static List<DayOfWeek> ToDays(SchoolWeekdays mask) =>
        new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Where(d => mask.HasFlag(Flag(d)))
            .ToList();
}

/// <summary>
/// What one school-local date is: open or closed, why, and the times it runs to.
/// Times are null on a day the school is closed.
/// </summary>
public sealed record SchoolDayInfo(
    DateOnly Date,
    SchoolDayKind Kind,
    bool IsSchoolDay,
    string? Title,
    TimeOnly? StartTime,
    TimeOnly? LateArrivalAfter,
    TimeOnly? MissingCheckInAfter,
    TimeOnly? DismissalTime,
    int? CalendarDayId);

/// <summary>
/// The school's timetable for a range of dates: the regular week plus the calendar
/// exceptions that fall in the range. Pure and immutable — every rule about which
/// dates are school days and what is late on them is decided here, so callers never
/// compare against a cutoff of their own.
/// </summary>
public sealed class SchoolSchedule
{
    private readonly Dictionary<DateOnly, SchoolCalendarDay> _exceptions;

    public SchoolSchedule(SchoolTimingSettings regular, IEnumerable<SchoolCalendarDay> exceptions)
    {
        Regular = regular;
        _exceptions = exceptions
            .Where(e => e.Status == RecordStatus.Active)
            .GroupBy(e => e.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.UpdatedAt).First());
    }

    public SchoolTimingSettings Regular { get; }

    public SchoolDayInfo For(DateOnly date)
    {
        var regularDay = Regular.WorkingDays.HasFlag(SchoolWeek.Flag(date.DayOfWeek));
        if (!_exceptions.TryGetValue(date, out var ex))
            return regularDay
                ? Open(date, SchoolDayKind.Regular, null, null, null, null, null)
                : Closed(date, SchoolDayKind.NonWorkingDay, null, null);

        return ex.DayType switch
        {
            SchoolDayType.Holiday => Closed(date, SchoolDayKind.Holiday, ex.Title, ex.SchoolCalendarDayId),
            SchoolDayType.HalfDay => Open(date, SchoolDayKind.HalfDay, ex.Title, ex.SchoolCalendarDayId, ex.StartTime, ex.LateArrivalAfter, ex.DismissalTime),
            SchoolDayType.SpecialTiming => Open(date, SchoolDayKind.SpecialTiming, ex.Title, ex.SchoolCalendarDayId, ex.StartTime, ex.LateArrivalAfter, ex.DismissalTime),
            _ => Open(date, SchoolDayKind.WorkingDay, ex.Title, ex.SchoolCalendarDayId, ex.StartTime, ex.LateArrivalAfter, ex.DismissalTime)
        };
    }

    public bool IsSchoolDay(DateOnly date) => For(date).IsSchoolDay;

    /// <summary>
    /// An arrival is late when it is after that date's late cutoff. An arrival on a day
    /// the school is closed is off-schedule, not late.
    /// </summary>
    public bool IsLate(DateOnly date, TimeOnly time) =>
        For(date) is { IsSchoolDay: true, LateArrivalAfter: { } cutoff } && time > cutoff;

    /// <summary>School days in the inclusive range <paramref name="from"/>..<paramref name="to"/>.</summary>
    public IEnumerable<DateOnly> SchoolDaysBetween(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsSchoolDay(d)) yield return d;
    }

    public int CountSchoolDays(DateOnly from, DateOnly to) => from > to ? 0 : SchoolDaysBetween(from, to).Count();

    private SchoolDayInfo Open(DateOnly date, SchoolDayKind kind, string? title, int? id,
        TimeOnly? start, TimeOnly? late, TimeOnly? dismissal)
    {
        var lateAfter = late ?? Regular.LateArrivalAfter;
        // The missing check-in alert keeps the regular gap after the late cutoff, so a day
        // that starts later raises it correspondingly later.
        var gap = Regular.MissingCheckInAfter.ToTimeSpan() - Regular.LateArrivalAfter.ToTimeSpan();
        if (gap < TimeSpan.Zero) gap = TimeSpan.Zero;
        var missing = AddClamped(lateAfter, gap);
        return new SchoolDayInfo(date, kind, true, title, start ?? Regular.StartTime, lateAfter, missing, dismissal ?? Regular.DismissalTime, id);
    }

    private static SchoolDayInfo Closed(DateOnly date, SchoolDayKind kind, string? title, int? id) =>
        new(date, kind, false, title, null, null, null, null, id);

    private static TimeOnly AddClamped(TimeOnly time, TimeSpan add)
    {
        var total = time.ToTimeSpan() + add;
        var max = new TimeSpan(23, 59, 0);
        return TimeOnly.FromTimeSpan(total > max ? max : total);
    }
}
