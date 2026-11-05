using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IAttendanceJourneyService
{
    Task<List<AttendanceRecordDto>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetJourneyHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default);

    /// <summary>Live journey board: every active student's current position in the journey cycle.</summary>
    Task<List<StudentJourneyStatusDto>> GetLiveStatusesAsync(CancellationToken ct = default);
}
