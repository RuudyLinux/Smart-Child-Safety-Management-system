using ChildGuard.Application.DTOs;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
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

    public Task<List<GuardianFaceReference>> GetActiveReferencesAsync(int guardianId, CancellationToken ct = default) =>
        _db.GuardianFaceReferences
            .Where(r => r.GuardianId == guardianId && r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

    public Task<GuardianFaceReference?> GetReferenceAsync(int guardianId, int referenceId, CancellationToken ct = default) =>
        _db.GuardianFaceReferences.FirstOrDefaultAsync(r => r.GuardianId == guardianId && r.Id == referenceId, ct);

    public async Task AddReferenceAsync(GuardianFaceReference reference, CancellationToken ct = default) =>
        await _db.GuardianFaceReferences.AddAsync(reference, ct);

    public async Task AddSessionAsync(VerificationSession session, CancellationToken ct = default) =>
        await _db.VerificationSessions.AddAsync(session, ct);

    public Task<VerificationSession?> GetSessionAsync(int id, CancellationToken ct = default) =>
        _db.VerificationSessions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<bool> IsNeutralFrameHashUsedAsync(string hash, CancellationToken ct = default) =>
        _db.VerificationSessions.AnyAsync(s => s.NeutralFrameHash == hash, ct);

    public async Task CloseOpenSessionsAsync(int studentId, int adminId, CancellationToken ct = default)
    {
        var open = await _db.VerificationSessions
            .Where(s => s.StudentId == studentId && s.CreatedByAdminId == adminId && s.State == VerificationSessionState.Open)
            .ToListAsync(ct);
        foreach (var s in open) { s.State = VerificationSessionState.Closed; s.Version++; }
    }

    public async Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default) =>
        await _db.GuardianVerifications.AddAsync(verification, ct);

    public Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default) =>
        _db.GuardianVerifications.Include(v => v.Student).Include(v => v.Guardian)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, DateTime? fromUtc = null, DateTime? toUtcExclusive = null, CancellationToken ct = default)
    {
        var query = _db.GuardianVerifications.Include(v => v.Student).Include(v => v.Guardian).AsQueryable();
        if (studentId is not null) query = query.Where(v => v.StudentId == studentId);
        if (fromUtc is not null) query = query.Where(v => v.AttemptedAt >= fromUtc);
        if (toUtcExclusive is not null) query = query.Where(v => v.AttemptedAt < toUtcExclusive);
        return query.OrderByDescending(v => v.AttemptedAt).ToListAsync(ct);
    }

    public async Task<(List<GuardianVerification> Items, int Total)> GetHistoryPageAsync(
        VerificationHistoryFilter filter, DateTime? fromUtc, DateTime? toUtcExclusive, PageRequest page, CancellationToken ct = default)
    {
        var query = _db.GuardianVerifications.AsNoTracking().AsQueryable();
        if (filter.StudentId is int studentId) query = query.Where(v => v.StudentId == studentId);
        if (fromUtc is not null) query = query.Where(v => v.AttemptedAt >= fromUtc);
        if (toUtcExclusive is not null) query = query.Where(v => v.AttemptedAt < toUtcExclusive);
        if (filter.Result is VerificationResult result) query = query.Where(v => v.Result == result);
        if (filter.Approved is bool approved) query = query.Where(v => v.PickupApproved == approved);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            // Student or guardian name; case-insensitive on both engines.
            if (_db.Database.IsNpgsql())
            {
                var pattern = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = query.Where(v => EF.Functions.ILike(v.Student.FullName, pattern, "\\") || EF.Functions.ILike(v.Guardian.FullName, pattern, "\\"));
            }
            else
                query = query.Where(v => v.Student.FullName.Contains(text) || v.Guardian.FullName.Contains(text));
        }

        var total = await query.CountAsync(ct);

        var desc = page.Descending;
        IOrderedQueryable<GuardianVerification> ordered = page.Sort switch
        {
            "score" => desc ? query.OrderByDescending(v => v.MatchScore) : query.OrderBy(v => v.MatchScore),
            // Lower-cased: MySQL compares names case-insensitively, PostgreSQL does not.
            "student" => desc ? query.OrderByDescending(v => v.Student.FullName.ToLower()) : query.OrderBy(v => v.Student.FullName.ToLower()),
            "guardian" => desc ? query.OrderByDescending(v => v.Guardian.FullName.ToLower()) : query.OrderBy(v => v.Guardian.FullName.ToLower()),
            "when" => desc ? query.OrderByDescending(v => v.AttemptedAt) : query.OrderBy(v => v.AttemptedAt),
            // No sort given: newest first, as the unpaged log is.
            _ => query.OrderByDescending(v => v.AttemptedAt)
        };
        var items = await ordered.ThenByDescending(v => v.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Include(v => v.Student).Include(v => v.Guardian)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<VerificationHistorySummaryDto> GetHistorySummaryAsync(int? studentId, DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct = default)
    {
        var query = _db.GuardianVerifications.AsNoTracking().AsQueryable();
        if (studentId is not null) query = query.Where(v => v.StudentId == studentId);
        if (fromUtc is not null) query = query.Where(v => v.AttemptedAt >= fromUtc);
        if (toUtcExclusive is not null) query = query.Where(v => v.AttemptedAt < toUtcExclusive);
        return new VerificationHistorySummaryDto(
            Total: await query.CountAsync(ct),
            Matched: await query.CountAsync(v => v.Result == VerificationResult.Match, ct),
            NoMatch: await query.CountAsync(v => v.Result == VerificationResult.NoMatch, ct),
            Approved: await query.CountAsync(v => v.PickupApproved, ct));
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
        catch (DbUpdateException ex) when (ChildGuard.Infrastructure.Persistence.DbErrors.IsUniqueViolation(ex))
        {
            // A unique index refused the write: a live capture submitted twice, or a face registered twice at once.
            throw new ConcurrencyConflictException("That was just recorded by someone else. Start again.");
        }
    }
}
