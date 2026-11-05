using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class PhotoRepository : IPhotoRepository
{
    private readonly ChildGuardDbContext _db;
    public PhotoRepository(ChildGuardDbContext db) => _db = db;

    public Task<GuardianPhoto?> GetGuardianPhotoAsync(int guardianId, CancellationToken ct = default) =>
        _db.GuardianPhotos.FirstOrDefaultAsync(p => p.GuardianId == guardianId, ct);

    public Task<StudentPhoto?> GetStudentPhotoAsync(int studentId, CancellationToken ct = default) =>
        _db.StudentPhotos.FirstOrDefaultAsync(p => p.StudentId == studentId, ct);

    public Task<bool> GuardianExistsAsync(int guardianId, CancellationToken ct = default) =>
        _db.Guardians.AnyAsync(g => g.GuardianId == guardianId, ct);

    public Task<bool> StudentExistsAsync(int studentId, CancellationToken ct = default) =>
        _db.Students.AnyAsync(s => s.StudentId == studentId, ct);

    public async Task AddAsync(ProfilePhoto photo, CancellationToken ct = default)
    {
        switch (photo)
        {
            case GuardianPhoto g: await _db.GuardianPhotos.AddAsync(g, ct); break;
            case StudentPhoto s: await _db.StudentPhotos.AddAsync(s, ct); break;
            default: throw new ArgumentOutOfRangeException(nameof(photo), photo.GetType().Name, "Unsupported photo owner.");
        }
    }

    public void Remove(ProfilePhoto photo)
    {
        switch (photo)
        {
            case GuardianPhoto g: _db.GuardianPhotos.Remove(g); break;
            case StudentPhoto s: _db.StudentPhotos.Remove(s); break;
            default: throw new ArgumentOutOfRangeException(nameof(photo), photo.GetType().Name, "Unsupported photo owner.");
        }
    }

    public async Task<Dictionary<int, DateTime>> GetGuardianPhotoTimestampsAsync(
        IReadOnlyCollection<int>? guardianIds = null, CancellationToken ct = default)
    {
        // Projects the two scalar columns only — the blob is never pulled into memory here.
        var query = _db.GuardianPhotos.AsNoTracking().Select(p => new { p.GuardianId, p.UpdatedAt });
        if (guardianIds is { Count: > 0 }) query = query.Where(p => guardianIds.Contains(p.GuardianId));
        return await query.ToDictionaryAsync(p => p.GuardianId, p => p.UpdatedAt, ct);
    }

    public async Task<Dictionary<int, DateTime>> GetStudentPhotoTimestampsAsync(
        IReadOnlyCollection<int>? studentIds = null, CancellationToken ct = default)
    {
        var query = _db.StudentPhotos.AsNoTracking().Select(p => new { p.StudentId, p.UpdatedAt });
        if (studentIds is { Count: > 0 }) query = query.Where(p => studentIds.Contains(p.StudentId));
        return await query.ToDictionaryAsync(p => p.StudentId, p => p.UpdatedAt, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
