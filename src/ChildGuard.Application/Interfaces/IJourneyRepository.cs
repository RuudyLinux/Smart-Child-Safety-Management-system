using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public interface IJourneyRepository
{
    Task<JourneyEvent?> GetLastEventAsync(int studentId, CancellationToken ct = default);
    Task AddJourneyEventAsync(JourneyEvent journeyEvent, CancellationToken ct = default);
    Task AddAttendanceAsync(Attendance attendance, CancellationToken ct = default);
    Task<List<JourneyEvent>> GetHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<JourneyEvent>> GetRecentAsync(int take, CancellationToken ct = default);
    Task<List<Attendance>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
