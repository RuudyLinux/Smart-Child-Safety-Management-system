using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IAttendanceJourneyService
{
    Task<List<AttendanceRecordDto>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetJourneyHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<JourneyEventDto>> GetRecentEventsAsync(int take, CancellationToken ct = default);

    /// <summary>Date-bounded, paged journey report. Fails on an inverted or over-long range.</summary>
    Task<Result<JourneyReportPageDto>> GetJourneyReportAsync(
        DateOnly from, DateOnly to, int? studentId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Live journey board: every active student's current position in the journey cycle.</summary>
    Task<List<StudentJourneyStatusDto>> GetLiveStatusesAsync(CancellationToken ct = default);
}
