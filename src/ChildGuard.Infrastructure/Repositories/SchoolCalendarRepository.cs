using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class SchoolCalendarRepository : ISchoolCalendarRepository
{
    private readonly ChildGuardDbContext _db;
    public SchoolCalendarRepository(ChildGuardDbContext db) => _db = db;

    public Task<SchoolTimingSettings?> GetSettingsAsync(bool track, CancellationToken ct = default)
    {
        var query = track ? _db.SchoolTimingSettings : _db.SchoolTimingSettings.AsNoTracking();
        return query.FirstOrDefaultAsync(s => s.SchoolTimingSettingsId == SchoolTimingSettings.SingletonId, ct);
    }

    public Task<List<SchoolCalendarDay>> GetDaysAsync(DateOnly from, DateOnly to, bool activeOnly, CancellationToken ct = default) =>
        _db.SchoolCalendarDays.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to && (!activeOnly || d.Status == RecordStatus.Active))
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

    public Task<SchoolCalendarDay?> GetDayByIdAsync(int id, CancellationToken ct = default) =>
        _db.SchoolCalendarDays.FirstOrDefaultAsync(d => d.SchoolCalendarDayId == id, ct);

    public Task<SchoolCalendarDay?> GetDayByDateAsync(DateOnly date, CancellationToken ct = default) =>
        _db.SchoolCalendarDays.FirstOrDefaultAsync(d => d.Date == date, ct);

    public void AddSettings(SchoolTimingSettings settings) => _db.SchoolTimingSettings.Add(settings);
    public void AddDay(SchoolCalendarDay day) => _db.SchoolCalendarDays.Add(day);
    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
