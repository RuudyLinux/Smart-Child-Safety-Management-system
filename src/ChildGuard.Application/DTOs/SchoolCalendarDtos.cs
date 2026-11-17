using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <summary>The school's regular week.</summary>
/// <param name="IsCustomized">False while the values still come from configuration (nothing saved yet).</param>
public record SchoolTimingSettingsDto(
    List<DayOfWeek> WorkingDays,
    TimeOnly StartTime,
    TimeOnly LateArrivalAfter,
    TimeOnly MissingCheckInAfter,
    TimeOnly DismissalTime,
    int PickupGraceMinutes,
    int AttendanceWindowDays,
    string TimeZone,
    bool IsCustomized,
    DateTime? UpdatedAt);

public record UpdateSchoolTimingSettingsRequest(
    List<DayOfWeek> WorkingDays,
    TimeOnly StartTime,
    TimeOnly LateArrivalAfter,
    TimeOnly MissingCheckInAfter,
    TimeOnly DismissalTime,
    int PickupGraceMinutes,
    int AttendanceWindowDays);

/// <summary>A calendar exception (holiday, half day, special timing, extra working day).</summary>
public record SchoolCalendarDayDto(
    int Id,
    DateOnly Date,
    SchoolDayType DayType,
    string Title,
    TimeOnly? StartTime,
    TimeOnly? LateArrivalAfter,
    TimeOnly? DismissalTime,
    RecordStatus Status,
    DateTime UpdatedAt);

public record SchoolCalendarDayRequest(
    DateOnly Date,
    SchoolDayType DayType,
    string Title,
    TimeOnly? StartTime,
    TimeOnly? LateArrivalAfter,
    TimeOnly? DismissalTime);

/// <summary>One resolved date: what kind of day it is and the times it runs to (null when closed).</summary>
public record SchoolDayDto(
    DateOnly Date,
    DayOfWeek DayOfWeek,
    SchoolDayKind Kind,
    bool IsSchoolDay,
    string? Title,
    TimeOnly? StartTime,
    TimeOnly? LateArrivalAfter,
    TimeOnly? MissingCheckInAfter,
    TimeOnly? DismissalTime,
    int? CalendarDayId);

/// <summary>Every date in a range, resolved, plus the regular week it was resolved against.</summary>
public record SchoolCalendarViewDto(
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    SchoolTimingSettingsDto Settings,
    List<SchoolDayDto> Days,
    int SchoolDays);
