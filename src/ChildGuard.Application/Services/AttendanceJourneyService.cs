using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class AttendanceJourneyService : IAttendanceJourneyService
{
    private readonly IJourneyRepository _journey;
    private readonly ISafetyReadService _safety;
    private readonly SchoolOptions _school;

    public AttendanceJourneyService(IJourneyRepository journey, ISafetyReadService safety, SchoolOptions school)
    {
        _journey = journey;
        _safety = safety;
        _school = school;
    }

    public async Task<List<AttendanceRecordDto>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var lateAfter = _school.LateAfterTime;
        return (await _journey.GetAttendanceAsync(studentId, from, to, ct))
            .Select(a => new AttendanceRecordDto(
                a.AttendanceId, a.StudentId, a.Student.FullName, a.Student.Grade,
                a.EventType, a.Date, a.Time, a.Status,
                // Only an arrival can be late; a departure has no such policy.
                a.EventType == AttendanceEventType.SchoolArrival && a.Time > lateAfter))
            .ToList();
    }

    public async Task<List<JourneyEventDto>> GetJourneyHistoryAsync(int studentId, CancellationToken ct = default) =>
        (await _journey.GetHistoryAsync(studentId, ct))
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student?.FullName ?? string.Empty, e.EventType, e.Timestamp, e.Source))
            .ToList();

    public const int MaxRecentTake = 200;
    public const int MaxReportPageSize = 200;
    public const int MaxReportRangeDays = 366;

    public async Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default) =>
        (await _journey.GetRecentAsync(Math.Clamp(take, 1, MaxRecentTake), ct))
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToList();

    public async Task<Result<JourneyReportPageDto>> GetJourneyReportAsync(
        DateOnly from, DateOnly to, int? studentId, int page, int pageSize, CancellationToken ct = default)
    {
        if (to < from)
            return Result<JourneyReportPageDto>.Failure("'to' must be on or after 'from'.");
        if (to.DayNumber - from.DayNumber + 1 > MaxReportRangeDays)
            return Result<JourneyReportPageDto>.Failure($"Date range cannot exceed {MaxReportRangeDays} days.");

        page = Math.Clamp(page, 1, 100_000); // upper bound keeps (page - 1) * pageSize from overflowing
        pageSize = Math.Clamp(pageSize, 1, MaxReportPageSize);

        // Inclusive calendar dates -> half-open UTC instant range, so an event at
        // 23:59:59.9 on 'to' is included and nothing from the next day is.
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var (items, total) = await _journey.GetPageAsync(fromUtc, toUtcExclusive, studentId, (page - 1) * pageSize, pageSize, ct);
        var dtos = items
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student?.FullName ?? string.Empty, e.EventType, e.Timestamp, e.Source))
            .ToList();
        return Result<JourneyReportPageDto>.Success(new JourneyReportPageDto(dtos, total, page, pageSize, from, to, studentId));
    }

    public Task<List<StudentJourneyStatusDto>> GetLiveStatusesAsync(CancellationToken ct = default) =>
        _safety.GetLiveJourneyStatusesAsync(ct);
}
