using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Interfaces;

public interface IJourneyRepository
{
    /// <summary>
    /// Latest event for the student at or after <paramref name="sinceUtc"/>. Scan
    /// validation passes the start of the current school day so a cycle left open
    /// yesterday (e.g. no departure scan) cannot block today's first checkpoint.
    /// </summary>
    Task<JourneyEvent?> GetLastEventSinceAsync(int studentId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> while holding an exclusive lock on the student's
    /// row, inside one transaction. Every read-validate-write of the journey state
    /// machine goes through here, so two concurrent scans/approvals for the same
    /// student are applied one after the other instead of both passing validation
    /// against the same stale "last event". Changes saved inside commit together.
    /// </summary>
    Task<T> RunSerializedForStudentAsync<T>(int studentId, Func<CancellationToken, Task<T>> work, CancellationToken ct = default);

    /// <summary>The event a scanner already recorded under this idempotency key, if any.</summary>
    Task<JourneyEvent?> GetByScanIdAsync(int rfidDeviceId, string scanId, CancellationToken ct = default);

    Task AddJourneyEventAsync(JourneyEvent journeyEvent, CancellationToken ct = default);
    Task AddAttendanceAsync(Attendance attendance, CancellationToken ct = default);
    Task<List<JourneyEvent>> GetHistoryAsync(int studentId, CancellationToken ct = default);
    Task<List<JourneyEvent>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// One page of events with Timestamp in [fromUtc, toUtcExclusive), newest first,
    /// plus the total number of matching events so callers can tell when a page is
    /// not the whole result.
    /// </summary>
    Task<(List<JourneyEvent> Items, int TotalCount)> GetPageAsync(
        DateTime fromUtc, DateTime toUtcExclusive, int? studentId, int skip, int take, CancellationToken ct = default);

    Task<List<Attendance>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
