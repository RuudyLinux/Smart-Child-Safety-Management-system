using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class TemporaryPickupRepository : ITemporaryPickupRepository
{
    private readonly ChildGuardDbContext _db;
    public TemporaryPickupRepository(ChildGuardDbContext db) => _db = db;

    public async Task AddAsync(TemporaryPickupAuthorization authorization, CancellationToken ct = default) =>
        await _db.TemporaryPickupAuthorizations.AddAsync(authorization, ct);

    public async Task<TemporaryPickupAuthorization?> GetAsync(int id, CancellationToken ct = default)
    {
        var auth = await _db.TemporaryPickupAuthorizations
            .Include(a => a.Student)
            .Include(a => a.RequestedByGuardian)
            .Include(a => a.DecidedByAdmin)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (auth is null) return null;

        // Presence only: the photo bytes are loaded separately, when they are actually served.
        if (auth.Photo is null && await _db.TemporaryPickupPhotos.AnyAsync(p => p.TemporaryPickupAuthorizationId == id, ct))
            auth.Photo = await _db.TemporaryPickupPhotos.AsNoTracking()
                .Where(p => p.TemporaryPickupAuthorizationId == id)
                .Select(p => new TemporaryPickupPhoto { Id = p.Id, TemporaryPickupAuthorizationId = id, ContentType = p.ContentType, ByteSize = p.ByteSize, UpdatedAt = p.UpdatedAt, Data = Array.Empty<byte>() })
                .FirstAsync(ct);
        return auth;
    }

    public Task<TemporaryPickupPhoto?> GetPhotoAsync(int id, CancellationToken ct = default) =>
        _db.TemporaryPickupPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.TemporaryPickupAuthorizationId == id, ct);

    public Task<List<TemporaryPickupAuthorization>> ListAsync(
        int? studentId, IReadOnlyCollection<int>? studentIds, DateTime? since, CancellationToken ct = default)
    {
        var q = _db.TemporaryPickupAuthorizations.AsNoTracking()
            .Include(a => a.Student)
            .Include(a => a.RequestedByGuardian)
            .Include(a => a.DecidedByAdmin)
            .AsQueryable();
        if (studentId is not null) q = q.Where(a => a.StudentId == studentId);
        if (studentIds is not null) q = q.Where(a => studentIds.Contains(a.StudentId));
        // Anything still open is listed whatever its age; closed ones only within the look-back.
        if (since is not null) q = q.Where(a => a.CreatedAt >= since || a.ValidUntilUtc >= since);
        return q.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
    }

    public Task<bool> HasOverlappingOpenAsync(int studentId, DateTime fromUtc, DateTime untilUtc, CancellationToken ct = default) =>
        _db.TemporaryPickupAuthorizations.AnyAsync(a =>
            a.StudentId == studentId
            && (a.Status == TemporaryPickupStatus.Pending || a.Status == TemporaryPickupStatus.Approved)
            && a.ValidFromUtc < untilUtc
            && a.ValidUntilUtc > fromUtc, ct);

    public async Task<HashSet<int>> GetPhotoIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        (await _db.TemporaryPickupPhotos.AsNoTracking()
            .Where(p => ids.Contains(p.TemporaryPickupAuthorizationId))
            .Select(p => p.TemporaryPickupAuthorizationId)
            .ToListAsync(ct)).ToHashSet();

    public async Task AddAuditAsync(TemporaryPickupAuditEntry entry, CancellationToken ct = default) =>
        await _db.TemporaryPickupAuditEntries.AddAsync(entry, ct);

    public Task<List<TemporaryPickupAuditEntry>> GetAuditAsync(int id, CancellationToken ct = default) =>
        _db.TemporaryPickupAuditEntries.AsNoTracking()
            .Where(a => a.AuthorizationId == id)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException("This temporary pickup was just changed by someone else. Reload and try again.");
        }
    }
}
