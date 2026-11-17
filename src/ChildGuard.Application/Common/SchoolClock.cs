namespace ChildGuard.Application.Common;

/// <summary>
/// The school's calendar, in the configured school time zone. Timestamps are stored
/// as UTC instants; everything calendar-shaped (what "today" is, when a school day
/// starts, which date an arrival belongs to, whether it was after the late cutoff)
/// is decided here so no caller converts on its own.
/// </summary>
public sealed class SchoolClock
{
    private readonly TimeProvider _time;

    public SchoolClock(SchoolOptions options, TimeProvider time)
    {
        _time = time;
        TimeZone = Resolve(options.TimeZone);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>Current wall-clock time at the school.</summary>
    public DateTime LocalNow => ToLocal(UtcNow);

    /// <summary>Today's date at the school.</summary>
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    /// <summary>School-local wall-clock time of a UTC instant.</summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    /// <summary>The school-local (date, time) an instant falls on — what Attendance stores.</summary>
    public (DateOnly Date, TimeOnly Time) ToSchoolDateTime(DateTime utc)
    {
        var local = ToLocal(utc);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>UTC instant at which the given school date begins (local midnight).</summary>
    public DateTime StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A DST jump can skip local midnight; the day then starts at the first valid minute.
        while (TimeZone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
    }

    /// <summary>Start of the current school day as a UTC instant.</summary>
    public DateTime TodayStartUtc => StartOfDayUtc(Today);

    /// <summary>Inclusive school-date range as a half-open UTC instant range.</summary>
    public (DateTime FromUtc, DateTime ToUtcExclusive) RangeUtc(DateOnly from, DateOnly to) =>
        (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));

    private static TimeZoneInfo Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("School:TimeZone must be configured (e.g. \"Asia/Kolkata\").");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException($"School:TimeZone \"{id}\" is not a known time zone id.");
        }
    }
}
