using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Reads the saved regular week (or the configured one, until an admin saves it) and the
/// calendar exceptions in a range. Two small queries per call.
/// </summary>
public sealed class SchoolScheduleProvider : ISchoolScheduleProvider
{
    private readonly ISchoolCalendarRepository _repo;
    private readonly SchoolOptions _options;

    public SchoolScheduleProvider(ISchoolCalendarRepository repo, SchoolOptions options)
    {
        _repo = repo;
        _options = options;
    }

    public async Task<SchoolSchedule> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        var settings = await _repo.GetSettingsAsync(track: false, ct) ?? _options.ToDefaultSettings();
        var days = await _repo.GetDaysAsync(from, to, activeOnly: true, ct);
        return new SchoolSchedule(settings, days);
    }
}

/// <summary>
/// The configured regular week with no calendar exceptions. Used where no database is
/// wired (unit tests) so services keep their configured behaviour.
/// </summary>
public sealed class ConfiguredSchoolScheduleProvider : ISchoolScheduleProvider
{
    private readonly SchoolOptions _options;
    public ConfiguredSchoolScheduleProvider(SchoolOptions options) => _options = options;

    public Task<SchoolSchedule> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Task.FromResult(new SchoolSchedule(_options.ToDefaultSettings(), Array.Empty<SchoolCalendarDay>()));
}

public class SchoolCalendarService : ISchoolCalendarService
{
    public const int MaxRangeDays = 400;
    public const int MaxTitleLength = 100;
    /// <summary>Exceptions can be planned this many years either side of today.</summary>
    public const int PlanningYears = 2;

    public const string ExceptionNotFound = "Calendar day not found.";
    public const string NoWorkingDays = "Choose at least one school day in the week.";
    public const string StartAfterLate = "The late cutoff cannot be before the start time.";
    public const string LateAfterMissing = "The missing check-in time cannot be before the late cutoff.";
    public const string DismissalBeforeStart = "Dismissal must be after the start time.";
    public const string GraceOutOfRange = "Pickup grace must be between 0 and 240 minutes.";
    public const string WindowOutOfRange = "The attendance window must be between 1 and 366 days.";
    public const string TitleRequired = "Give the day a title (e.g. \"Diwali\").";
    public const string HolidayHasTimes = "A holiday has no timings. Remove the start, late and dismissal times.";
    public const string HalfDayNeedsDismissal = "A half day needs its dismissal time.";
    public const string SpecialTimingNeedsTime = "A special timing day needs at least one changed time.";
    public const string DuplicateDate = "This date already has a calendar entry. Edit that entry instead.";
    public const string InactiveExceptionIsHistory = "This entry has been removed. Add the date again instead.";

    private readonly ISchoolCalendarRepository _repo;
    private readonly ISchoolScheduleProvider _schedule;
    private readonly SchoolOptions _options;
    private readonly SchoolClock _clock;
    private readonly IAuditTrail _audit;

    public SchoolCalendarService(ISchoolCalendarRepository repo, ISchoolScheduleProvider schedule, SchoolOptions options,
        SchoolClock clock, IAuditTrail? audit = null)
    {
        _repo = repo;
        _schedule = schedule;
        _options = options;
        _clock = clock;
        _audit = audit ?? NullAuditTrail.Instance;
    }

    // ------------------------------------------------------------------ regular week

    public async Task<SchoolTimingSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var saved = await _repo.GetSettingsAsync(track: false, ct);
        return ToDto(saved ?? _options.ToDefaultSettings(), saved is not null);
    }

    public async Task<Result<SchoolTimingSettingsDto>> UpdateSettingsAsync(UpdateSchoolTimingSettingsRequest r, int? adminId, CancellationToken ct = default)
    {
        var mask = SchoolWeek.FromDays(r.WorkingDays ?? new List<DayOfWeek>());
        if (mask == SchoolWeekdays.None) return Result<SchoolTimingSettingsDto>.Failure(NoWorkingDays);
        var error = ValidateTimes(r.StartTime, r.LateArrivalAfter, r.DismissalTime)
            ?? (r.MissingCheckInAfter < r.LateArrivalAfter ? LateAfterMissing : null)
            ?? (r.PickupGraceMinutes is < 0 or > 240 ? GraceOutOfRange : null)
            ?? (r.AttendanceWindowDays is < 1 or > 366 ? WindowOutOfRange : null);
        if (error is not null) return Result<SchoolTimingSettingsDto>.Failure(error);

        var settings = await _repo.GetSettingsAsync(track: true, ct);
        var before = settings is null ? null : ToDto(settings, true);
        if (settings is null)
        {
            settings = new SchoolTimingSettings { SchoolTimingSettingsId = SchoolTimingSettings.SingletonId };
            _repo.AddSettings(settings);
        }

        settings.WorkingDays = mask;
        settings.StartTime = r.StartTime;
        settings.LateArrivalAfter = r.LateArrivalAfter;
        settings.MissingCheckInAfter = r.MissingCheckInAfter;
        settings.DismissalTime = r.DismissalTime;
        settings.PickupGraceMinutes = r.PickupGraceMinutes;
        settings.AttendanceWindowDays = r.AttendanceWindowDays;
        settings.UpdatedAt = _clock.UtcNow;
        settings.UpdatedByAdminId = adminId;

        var after = ToDto(settings, true);
        _audit.Record(new AuditEvent("SchoolTiming.Updated", nameof(SchoolTimingSettings), settings.SchoolTimingSettingsId,
            "Updated the school's regular timings")
        {
            Details = new { before = Describe(before), after = Describe(after) }
        });
        await _repo.SaveChangesAsync(ct);
        return Result<SchoolTimingSettingsDto>.Success(after);
    }

    // ------------------------------------------------------------------ resolved calendar

    public async Task<Result<SchoolCalendarViewDto>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rangeError = ValidateRange(from, to);
        if (rangeError is not null) return Result<SchoolCalendarViewDto>.Failure(rangeError);

        var schedule = await _schedule.GetAsync(from, to, ct);
        var saved = await _repo.GetSettingsAsync(track: false, ct);
        var days = new List<SchoolDayDto>();
        for (var d = from; d <= to; d = d.AddDays(1)) days.Add(ToDto(schedule.For(d)));

        return Result<SchoolCalendarViewDto>.Success(new SchoolCalendarViewDto(
            from, to, _clock.Today, ToDto(schedule.Regular, saved is not null), days, days.Count(d => d.IsSchoolDay)));
    }

    public async Task<SchoolDayDto> GetTodayAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        return ToDto((await _schedule.GetAsync(today, today, ct)).For(today));
    }

    // ------------------------------------------------------------------ exceptions

    public async Task<Result<List<SchoolCalendarDayDto>>> GetExceptionsAsync(DateOnly from, DateOnly to, bool includeInactive, CancellationToken ct = default)
    {
        var rangeError = ValidateRange(from, to);
        if (rangeError is not null) return Result<List<SchoolCalendarDayDto>>.Failure(rangeError);
        var rows = await _repo.GetDaysAsync(from, to, activeOnly: !includeInactive, ct);
        return Result<List<SchoolCalendarDayDto>>.Success(rows.OrderBy(d => d.Date).Select(ToDto).ToList());
    }

    public async Task<Result<SchoolCalendarDayDto>> CreateExceptionAsync(SchoolCalendarDayRequest r, CancellationToken ct = default)
    {
        var error = await ValidateExceptionAsync(r, ct);
        if (error is not null) return Result<SchoolCalendarDayDto>.Failure(error);

        var existing = await _repo.GetDayByDateAsync(r.Date, ct);
        if (existing is { Status: RecordStatus.Active }) return Result<SchoolCalendarDayDto>.Failure(DuplicateDate);

        var now = _clock.UtcNow;
        if (existing is not null)
        {
            // One row per date: a removed entry is brought back rather than duplicated.
            Apply(existing, r);
            existing.Status = RecordStatus.Active;
            existing.UpdatedAt = now;
            _audit.Record(Event("SchoolCalendar.Added", existing, $"Added {Label(existing)}"));
            await _repo.SaveChangesAsync(ct);
            return Result<SchoolCalendarDayDto>.Success(ToDto(existing));
        }

        var day = new SchoolCalendarDay { CreatedAt = now, UpdatedAt = now };
        Apply(day, r);
        _repo.AddDay(day);
        await _audit.SaveWithAuditAsync(_repo.SaveChangesAsync, () => Event("SchoolCalendar.Added", day, $"Added {Label(day)}"), ct);
        return Result<SchoolCalendarDayDto>.Success(ToDto(day));
    }

    public async Task<Result<SchoolCalendarDayDto>> UpdateExceptionAsync(int id, SchoolCalendarDayRequest r, CancellationToken ct = default)
    {
        var day = await _repo.GetDayByIdAsync(id, ct);
        if (day is null) return Result<SchoolCalendarDayDto>.Failure(ExceptionNotFound);
        if (day.Status != RecordStatus.Active) return Result<SchoolCalendarDayDto>.Failure(InactiveExceptionIsHistory);

        var error = await ValidateExceptionAsync(r, ct);
        if (error is not null) return Result<SchoolCalendarDayDto>.Failure(error);
        if (r.Date != day.Date && await _repo.GetDayByDateAsync(r.Date, ct) is not null)
            return Result<SchoolCalendarDayDto>.Failure(DuplicateDate);

        var before = ToDto(day);
        Apply(day, r);
        day.UpdatedAt = _clock.UtcNow;
        _audit.Record(Event("SchoolCalendar.Updated", day, $"Updated {Label(day)}") with { Details = new { before, after = ToDto(day) } });
        await _repo.SaveChangesAsync(ct);
        return Result<SchoolCalendarDayDto>.Success(ToDto(day));
    }

    public async Task<Result> DeactivateExceptionAsync(int id, CancellationToken ct = default)
    {
        var day = await _repo.GetDayByIdAsync(id, ct);
        if (day is null) return Result.Failure(ExceptionNotFound);
        if (day.Status != RecordStatus.Active) return Result.Success();
        day.Status = RecordStatus.Inactive;
        day.UpdatedAt = _clock.UtcNow;
        _audit.Record(Event("SchoolCalendar.Removed", day, $"Removed {Label(day)}"));
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ validation

    private string? ValidateRange(DateOnly from, DateOnly to)
    {
        if (to < from) return "'to' must be on or after 'from'.";
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays) return $"Date range cannot exceed {MaxRangeDays} days.";
        return null;
    }

    private static string? ValidateTimes(TimeOnly start, TimeOnly late, TimeOnly dismissal)
    {
        if (late < start) return StartAfterLate;
        if (dismissal <= start) return DismissalBeforeStart;
        return null;
    }

    private async Task<string?> ValidateExceptionAsync(SchoolCalendarDayRequest r, CancellationToken ct)
    {
        if (!Enum.IsDefined(r.DayType)) return "Choose what kind of day this is.";
        if (string.IsNullOrWhiteSpace(r.Title)) return TitleRequired;
        if (r.Title.Trim().Length > MaxTitleLength) return $"The title cannot be longer than {MaxTitleLength} characters.";
        var today = _clock.Today;
        if (r.Date < today.AddYears(-PlanningYears) || r.Date > today.AddYears(PlanningYears))
            return $"The date must be within {PlanningYears} years of today.";

        var hasTimes = r.StartTime is not null || r.LateArrivalAfter is not null || r.DismissalTime is not null;
        switch (r.DayType)
        {
            case SchoolDayType.Holiday when hasTimes: return HolidayHasTimes;
            case SchoolDayType.HalfDay when r.DismissalTime is null: return HalfDayNeedsDismissal;
            case SchoolDayType.SpecialTiming when !hasTimes: return SpecialTimingNeedsTime;
        }
        if (r.DayType == SchoolDayType.Holiday) return null;

        // The day's effective times (its own, else the regular week's) must still be in order.
        var regular = (await _schedule.GetAsync(r.Date, r.Date, ct)).Regular;
        return ValidateTimes(r.StartTime ?? regular.StartTime, r.LateArrivalAfter ?? regular.LateArrivalAfter, r.DismissalTime ?? regular.DismissalTime);
    }

    // ------------------------------------------------------------------ mapping

    private static void Apply(SchoolCalendarDay day, SchoolCalendarDayRequest r)
    {
        day.Date = r.Date;
        day.DayType = r.DayType;
        day.Title = r.Title.Trim();
        var holiday = r.DayType == SchoolDayType.Holiday;
        day.StartTime = holiday ? null : r.StartTime;
        day.LateArrivalAfter = holiday ? null : r.LateArrivalAfter;
        day.DismissalTime = holiday ? null : r.DismissalTime;
    }

    private static AuditEvent Event(string action, SchoolCalendarDay day, string summary) =>
        new(action, nameof(SchoolCalendarDay), day.SchoolCalendarDayId, summary) { Details = ToDto(day) };

    private static string Label(SchoolCalendarDay day) => $"{day.DayType} \"{day.Title}\" on {day.Date:yyyy-MM-dd}";

    private static object? Describe(SchoolTimingSettingsDto? s) => s is null ? null : new
    {
        workingDays = string.Join(",", s.WorkingDays),
        start = s.StartTime.ToString("HH:mm"),
        lateAfter = s.LateArrivalAfter.ToString("HH:mm"),
        missingCheckInAfter = s.MissingCheckInAfter.ToString("HH:mm"),
        dismissal = s.DismissalTime.ToString("HH:mm"),
        s.PickupGraceMinutes,
        s.AttendanceWindowDays
    };

    private SchoolTimingSettingsDto ToDto(SchoolTimingSettings s, bool customized) => new(
        SchoolWeek.ToDays(s.WorkingDays), s.StartTime, s.LateArrivalAfter, s.MissingCheckInAfter, s.DismissalTime,
        s.PickupGraceMinutes, s.AttendanceWindowDays, _clock.TimeZone.Id, customized, customized ? s.UpdatedAt : null);

    private static SchoolCalendarDayDto ToDto(SchoolCalendarDay d) =>
        new(d.SchoolCalendarDayId, d.Date, d.DayType, d.Title, d.StartTime, d.LateArrivalAfter, d.DismissalTime, d.Status, d.UpdatedAt);

    public static SchoolDayDto ToDto(SchoolDayInfo i) =>
        new(i.Date, i.Date.DayOfWeek, i.Kind, i.IsSchoolDay, i.Title, i.StartTime, i.LateArrivalAfter, i.MissingCheckInAfter, i.DismissalTime, i.CalendarDayId);
}
