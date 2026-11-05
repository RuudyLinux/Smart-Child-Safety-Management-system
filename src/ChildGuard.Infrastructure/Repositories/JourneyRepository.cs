using System.Data;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class JourneyRepository : IJourneyRepository
{
    private readonly ChildGuardDbContext _db;
    public JourneyRepository(ChildGuardDbContext db) => _db = db;

    public Task<JourneyEvent?> GetLastEventSinceAsync(int studentId, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.JourneyEvents
            .Where(e => e.StudentId == studentId && e.Timestamp >= sinceUtc)
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.JourneyEventId)
            .FirstOrDefaultAsync(ct);

    public async Task<T> RunSerializedForStudentAsync<T>(int studentId, Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        // Already inside a transaction (caller composed several steps): take the
        // lock in that transaction and let the caller own commit/rollback.
        if (_db.Database.CurrentTransaction is not null)
        {
            await LockStudentRowAsync(studentId, ct);
            return await work(ct);
        }

        // READ COMMITTED so the "last event" read after the lock sees whatever the
        // previous lock holder committed (under REPEATABLE READ the snapshot could
        // predate it).
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await LockStudentRowAsync(studentId, ct);
        var result = await work(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    // Row lock on the parent Students row: InnoDB holds it until commit, so a second
    // scan/approval for the same student waits here instead of racing validation.
    private Task LockStudentRowAsync(int studentId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT StudentId FROM Students WHERE StudentId = {studentId} FOR UPDATE", ct);

    public async Task AddJourneyEventAsync(JourneyEvent journeyEvent, CancellationToken ct = default) =>
        await _db.JourneyEvents.AddAsync(journeyEvent, ct);

    public async Task AddAttendanceAsync(Attendance attendance, CancellationToken ct = default) =>
        await _db.AttendanceRecords.AddAsync(attendance, ct);

    public Task<List<JourneyEvent>> GetHistoryAsync(int studentId, CancellationToken ct = default) =>
        _db.JourneyEvents
            .Include(e => e.Student)
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(ct);

    public Task<List<JourneyEvent>> GetRecentAsync(int take, CancellationToken ct = default) =>
        _db.JourneyEvents
            .Include(e => e.Student)
            .OrderByDescending(e => e.Timestamp)
            .Take(take)
            .ToListAsync(ct);

    public async Task<(List<JourneyEvent> Items, int TotalCount)> GetPageAsync(
        DateTime fromUtc, DateTime toUtcExclusive, int? studentId, int skip, int take, CancellationToken ct = default)
    {
        var query = _db.JourneyEvents.AsNoTracking()
            .Where(e => e.Timestamp >= fromUtc && e.Timestamp < toUtcExclusive);
        if (studentId is not null) query = query.Where(e => e.StudentId == studentId);

        var total = await query.CountAsync(ct);
        var items = total == 0
            ? new List<JourneyEvent>()
            : await query
                .Include(e => e.Student)
                .OrderByDescending(e => e.Timestamp)
                .ThenByDescending(e => e.JourneyEventId) // stable order across pages for equal timestamps
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        return (items, total);
    }

    public Task<List<Attendance>> GetAttendanceAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var query = _db.AttendanceRecords.Include(a => a.Student).AsQueryable();
        if (studentId is not null) query = query.Where(a => a.StudentId == studentId);
        if (from is not null) query = query.Where(a => a.Date >= from);
        if (to is not null) query = query.Where(a => a.Date <= to);
        return query.OrderByDescending(a => a.Date).ThenByDescending(a => a.Time).ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
