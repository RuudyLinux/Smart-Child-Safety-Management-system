using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;

namespace ChildGuard.Application.Services;

public class AttendanceJourneyService : IAttendanceJourneyService
{
    private readonly IJourneyRepository _journey;
    public AttendanceJourneyService(IJourneyRepository journey) => _journey = journey;

    public async Task<List<AttendanceRecordDto>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default) =>
        (await _journey.GetAttendanceAsync(studentId, from, to, ct))
            .Select(a => new AttendanceRecordDto(a.AttendanceId, a.StudentId, a.Student.FullName, a.EventType, a.Date, a.Time, a.Status))
            .ToList();

    public async Task<List<JourneyEventDto>> GetJourneyHistoryAsync(int studentId, CancellationToken ct = default) =>
        (await _journey.GetHistoryAsync(studentId, ct))
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student?.FullName ?? string.Empty, e.EventType, e.Timestamp, e.Source))
            .ToList();

    public async Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default) =>
        (await _journey.GetRecentAsync(take, ct))
            .Select(e => new JourneyEventDto(e.JourneyEventId, e.StudentId, e.Student.FullName, e.EventType, e.Timestamp, e.Source))
            .ToList();
}
