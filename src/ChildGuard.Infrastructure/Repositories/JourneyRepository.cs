using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class JourneyRepository : IJourneyRepository
{
    private readonly ChildGuardDbContext _db;
    public JourneyRepository(ChildGuardDbContext db) => _db = db;

    public Task<JourneyEvent?> GetLastEventAsync(int studentId, CancellationToken ct = default) =>
        _db.JourneyEvents
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(ct);

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
