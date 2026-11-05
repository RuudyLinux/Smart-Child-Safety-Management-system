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

    public async Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default) =>
        (await _journey.GetRecentAsync(take, ct))
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToList();

    public Task<List<StudentJourneyStatusDto>> GetLiveStatusesAsync(CancellationToken ct = default) =>
        _safety.GetLiveJourneyStatusesAsync(ct);
}
