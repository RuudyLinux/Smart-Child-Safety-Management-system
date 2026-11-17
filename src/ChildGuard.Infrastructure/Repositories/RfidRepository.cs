using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class RfidRepository : IRfidRepository
{
    private readonly ChildGuardDbContext _db;
    public RfidRepository(ChildGuardDbContext db) => _db = db;

    public Task<List<RfidCard>> GetAllAsync(CancellationToken ct = default) =>
        _db.RfidCards.Include(c => c.Student).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

    public async Task<(List<RfidCard> Items, int Total)> GetPageAsync(RfidCardListFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var query = _db.RfidCards.AsNoTracking();
        if (filter.Status is RfidCardStatus status) query = query.Where(c => c.Status == status);
        if (filter.Assigned is bool assigned) query = query.Where(c => (c.StudentId != null) == assigned);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            // The student's name, or the card UID as typed; case-insensitive on both engines.
            if (_db.Database.IsNpgsql())
            {
                var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(c => EF.Functions.ILike(c.CardUid, pattern, "\\")
                    || (c.Student != null && EF.Functions.ILike(c.Student.FullName, pattern, "\\")));
            }
            else
                query = query.Where(c => c.CardUid.Contains(text) || (c.Student != null && c.Student.FullName.Contains(text)));
        }

        var total = await query.CountAsync(ct);

        var desc = page.Descending;
        IOrderedQueryable<RfidCard> ordered = page.Sort switch
        {
            // Spares (no student) sort first when ascending; names compare case-insensitively on both engines.
            "student" => desc ? query.OrderByDescending(c => c.Student != null ? c.Student.FullName.ToLower() : "")
                              : query.OrderBy(c => c.Student != null ? c.Student.FullName.ToLower() : ""),
            "status" => (desc ? query.OrderByDescending(c => (int)c.Status) : query.OrderBy(c => (int)c.Status))
                .ThenByDescending(c => c.CreatedAt),
            "assigned" => desc ? query.OrderByDescending(c => c.AssignedAt ?? DateTime.MinValue) : query.OrderBy(c => c.AssignedAt ?? DateTime.MinValue),
            // No sort given: the newest cards first, as the unpaged list is.
            _ => query.OrderByDescending(c => c.CreatedAt)
        };
        var items = await ordered.ThenByDescending(c => c.RfidCardId)
            .Skip(page.Skip).Take(page.PageSize)
            .Include(c => c.Student)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<RfidCardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var cards = _db.RfidCards.AsNoTracking();
        return new RfidCardSummaryDto(
            Total: await cards.CountAsync(ct),
            Active: await cards.CountAsync(c => c.Status == RfidCardStatus.Active, ct),
            LostOrDamaged: await cards.CountAsync(c => c.Status == RfidCardStatus.Lost || c.Status == RfidCardStatus.Damaged, ct),
            AvailableSpare: await cards.CountAsync(c => c.Status == RfidCardStatus.Active && c.StudentId == null, ct),
            Unassigned: await cards.CountAsync(c => c.StudentId == null, ct));
    }

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
