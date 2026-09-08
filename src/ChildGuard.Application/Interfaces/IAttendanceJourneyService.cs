using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IAttendanceJourneyService
{
    Task<List<AttendanceRecordDto>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetJourneyHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default);
}
