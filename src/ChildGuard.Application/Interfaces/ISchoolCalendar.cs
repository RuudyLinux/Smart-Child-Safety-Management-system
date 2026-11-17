using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface ISchoolCalendarRepository
{
    /// <summary>The saved regular week, or null while configuration still supplies it.</summary>
    Task<SchoolTimingSettings?> GetSettingsAsync(bool track, CancellationToken ct = default);

    /// <summary>Exceptions dated in the inclusive range.</summary>
    Task<List<SchoolCalendarDay>> GetDaysAsync(DateOnly from, DateOnly to, bool activeOnly, CancellationToken ct = default);

    Task<SchoolCalendarDay?> GetDayByIdAsync(int id, CancellationToken ct = default);
    Task<SchoolCalendarDay?> GetDayByDateAsync(DateOnly date, CancellationToken ct = default);

    void AddSettings(SchoolTimingSettings settings);
    void AddDay(SchoolCalendarDay day);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Loads the school's timetable for a date range. The only source of late cutoffs and school days.</summary>
public interface ISchoolScheduleProvider
{
    Task<SchoolSchedule> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

public interface ISchoolCalendarService
{
    Task<SchoolTimingSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<SchoolTimingSettingsDto>> UpdateSettingsAsync(UpdateSchoolTimingSettingsRequest request, int? adminId, CancellationToken ct = default);

    /// <summary>Every date in the inclusive range, resolved against the regular week and exceptions.</summary>
    Task<Result<SchoolCalendarViewDto>> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Today at the school.</summary>
    Task<SchoolDayDto> GetTodayAsync(CancellationToken ct = default);

    Task<Result<List<SchoolCalendarDayDto>>> GetExceptionsAsync(DateOnly from, DateOnly to, bool includeInactive, CancellationToken ct = default);
    Task<Result<SchoolCalendarDayDto>> CreateExceptionAsync(SchoolCalendarDayRequest request, CancellationToken ct = default);
    Task<Result<SchoolCalendarDayDto>> UpdateExceptionAsync(int id, SchoolCalendarDayRequest request, CancellationToken ct = default);
    Task<Result> DeactivateExceptionAsync(int id, CancellationToken ct = default);
}
