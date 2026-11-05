namespace ChildGuard.AdminWeb.Services;

/// <summary>
/// Display-side school clock. The API returns instants in UTC; every page shows them
/// in the school's time zone (School:TimeZone — must match the API's setting), never in
/// whatever zone the web server happens to run in. Configured once at startup.
/// </summary>
public static class SchoolTime
{
    private static TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public static TimeZoneInfo Zone => _zone;

    public static void Configure(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new InvalidOperationException("School:TimeZone must be configured (e.g. \"Asia/Kolkata\").");
        _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    /// <summary>Current wall-clock time at the school.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone);

    /// <summary>Today's date at the school, as a midnight DateTime.</summary>
    public static DateTime Today => Now.Date;

    public static DateOnly TodayDate => DateOnly.FromDateTime(Now);

    /// <summary>
    /// School-local wall-clock time of an API timestamp. Values without a Kind came from
    /// the database as UTC; Local values (should not occur) are normalised first.
    /// </summary>
    public static DateTime ToSchoolTime(this DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, _zone);
    }
}
