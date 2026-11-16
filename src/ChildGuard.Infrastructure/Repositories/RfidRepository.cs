using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class RfidRepository : IRfidRepository
{
    private readonly ChildGuardDbContext _db;
    public RfidRepository(ChildGuardDbContext db) => _db = db;

    public Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public Task<RfidCard?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).FirstOrDefaultAsync(c => c.RfidCardId == id, ct);

    public Task<RfidCard?> GetByCardUidAsync(string cardUid, CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).FirstOrDefaultAsync(c => c.CardUid == cardUid, ct);

    public async Task AddAsync(RfidCard card, CancellationToken ct = default) =>
        await _db.RfidCards.AddAsync(card, ct);

    public async Task AddAssignmentHistoryAsync(RfidCardAssignment entry, CancellationToken ct = default) =>
        await _db.RfidCardAssignments.AddAsync(entry, ct);

    public async Task<Result> RunLockedAsync(IReadOnlyCollection<int> cardIds, Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            await LockCardsAsync(cardIds, ct);
            return await work(ct);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            await LockCardsAsync(cardIds, ct);
            var result = await work(ct);
            if (result.Succeeded) await tx.CommitAsync(ct);
            else
            {
                await tx.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
            }
            return result;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task LockCardsAsync(IReadOnlyCollection<int> cardIds, CancellationToken ct = default)
    {
        // Ascending order on every path, so two callers locking the same cards never deadlock.
        foreach (var id in cardIds.Distinct().OrderBy(i => i))
        {
            if (_db.Database.IsNpgsql())
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"RfidCardId\" FROM \"RfidCards\" WHERE \"RfidCardId\" = {id} FOR UPDATE", ct);
            else
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT RfidCardId FROM RfidCards WHERE RfidCardId = {id} FOR UPDATE", ct);
        }
        // A copy read before the lock may be stale: refresh it from the locked row.
        foreach (var entry in _db.ChangeTracker.Entries<RfidCard>().Where(e => cardIds.Contains(e.Entity.RfidCardId)).ToList())
            await entry.ReloadAsync(ct);
    }

    public Task<RfidCardAssignment?> GetOpenAssignmentAsync(int cardId, CancellationToken ct = default) =>
        _db.RfidCardAssignments.Where(h => h.RfidCardId == cardId && h.EndedAt == null)
            .OrderByDescending(h => h.Id).FirstOrDefaultAsync(ct);

    public async Task<List<RfidCardAssignmentDto>> GetHistoryAsync(int cardId, CancellationToken ct = default)
    {
        var rows = await (from h in _db.RfidCardAssignments.AsNoTracking()
                          where h.RfidCardId == cardId
                          join s in _db.Students.AsNoTracking() on h.StudentId equals s.StudentId
                          join a in _db.Admins.AsNoTracking() on h.AssignedByAdminId equals a.AdminId into assigned
                          from a in assigned.DefaultIfEmpty()
                          join e in _db.Admins.AsNoTracking() on h.EndedByAdminId equals e.AdminId into ended
                          from e in ended.DefaultIfEmpty()
                          orderby h.Id descending
                          select new { h, s.FullName, AssignedBy = a == null ? null : a.FullName, EndedBy = e == null ? null : e.FullName })
            .ToListAsync(ct);
        return rows.Select(r => new RfidCardAssignmentDto(r.h.Id, r.h.StudentId, r.FullName, r.h.AssignedAt, r.h.EndedAt, r.h.EndReason,
            r.h.IsMigratedSnapshot, r.AssignedBy, r.EndedBy)).ToList();
    }

    public async Task<bool> HasHistoryAsync(int cardId, CancellationToken ct = default) =>
        await _db.RfidCardAssignments.AnyAsync(h => h.RfidCardId == cardId, ct)
        || await _db.JourneyEvents.AnyAsync(e => e.RfidCardId == cardId, ct)
        || await _db.AttendanceRecords.AnyAsync(a => a.RfidCardId == cardId, ct);

    public void Remove(RfidCard card) => _db.RfidCards.Remove(card);

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<List<RfidDevice>> GetDevicesAsync(CancellationToken ct = default) =>
        _db.RfidDevices.ToListAsync(ct);

    public Task<RfidDevice?> GetDeviceByIdAsync(int id, CancellationToken ct = default) =>
        _db.RfidDevices.FirstOrDefaultAsync(d => d.RfidDeviceId == id, ct);

    public async Task AddDeviceAsync(RfidDevice device, CancellationToken ct = default) =>
        await _db.RfidDevices.AddAsync(device, ct);

    public Task<RfidDevice?> GetDeviceByKeyHashAsync(string keyHash, CancellationToken ct = default) =>
        _db.RfidDevices.AsNoTracking().FirstOrDefaultAsync(d => d.ApiKeyHash == keyHash, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
