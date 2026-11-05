using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class VerificationRepository : IVerificationRepository
{
    private readonly ChildGuardDbContext _db;
    public VerificationRepository(ChildGuardDbContext db) => _db = db;

    public Task<GuardianFaceData?> GetFaceDataAsync(int guardianId, CancellationToken ct = default) =>
        _db.GuardianFaceData.FirstOrDefaultAsync(f => f.GuardianId == guardianId, ct);

    public async Task UpsertFaceDataAsync(GuardianFaceData data, CancellationToken ct = default)
    {
        var existing = await _db.GuardianFaceData.FirstOrDefaultAsync(f => f.GuardianId == data.GuardianId, ct);
        if (existing is null)
        {
            await _db.GuardianFaceData.AddAsync(data, ct);
        }
        else
        {
            existing.EmbeddingJson = data.EmbeddingJson;
            existing.Model = data.Model;
            existing.IsActive = true;
            existing.RegisteredAt = DateTime.UtcNow;
        }
    }

    public async Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default) =>
        await _db.GuardianVerifications.AddAsync(verification, ct);

    public Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default) =>
        _db.GuardianVerifications.Include(v => v.Student).Include(v => v.Guardian)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, CancellationToken ct = default)
    {
        var query = _db.GuardianVerifications.Include(v => v.Student).Include(v => v.Guardian).AsQueryable();
        if (studentId is not null) query = query.Where(v => v.StudentId == studentId);
        return query.OrderByDescending(v => v.AttemptedAt).ToListAsync(ct);
    }

    public Task<List<GuardianVerification>> GetHistoryForGuardianAsync(int guardianId, int take, CancellationToken ct = default) =>
        _db.GuardianVerifications
            .Include(v => v.Student).Include(v => v.Guardian)
            .Where(v => v.GuardianId == guardianId)
            .OrderByDescending(v => v.AttemptedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<Dictionary<int, DateTime>> GetLastActivityByGuardianAsync(CancellationToken ct = default)
    {
        var rows = await _db.GuardianVerifications.AsNoTracking()
            .GroupBy(v => v.GuardianId)
            .Select(g => new { GuardianId = g.Key, LastAt = g.Max(v => v.AttemptedAt) })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.GuardianId, r => r.LastAt);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(
                "This verification was just updated by someone else. Reload and try again.");
        }
    }
}
