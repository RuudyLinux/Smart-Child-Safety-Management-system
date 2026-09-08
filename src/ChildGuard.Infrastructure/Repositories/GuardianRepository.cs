using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class GuardianRepository : IGuardianRepository
{
    private readonly ChildGuardDbContext _db;
    public GuardianRepository(ChildGuardDbContext db) => _db = db;

    public Task<Guardian?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.Email == email, ct);

    public Task<Guardian?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Guardians.FirstOrDefaultAsync(g => g.GuardianId == id, ct);

    public Task<Guardian?> GetWithStudentsAsync(int id, CancellationToken ct = default) =>
        _db.Guardians
            .Include(g => g.FaceData)
            .Include(g => g.StudentGuardians).ThenInclude(sg => sg.Student)
            .FirstOrDefaultAsync(g => g.GuardianId == id, ct);

    public Task<List<Guardian>> GetAllAsync(CancellationToken ct = default) =>
        _db.Guardians
            .Include(g => g.StudentGuardians)
            .OrderBy(g => g.FullName)
            .ToListAsync(ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        _db.Guardians.AnyAsync(g => g.Email == email, ct);

    public async Task AddAsync(Guardian guardian, CancellationToken ct = default) =>
        await _db.Guardians.AddAsync(guardian, ct);

    public Task<string?> GetFcmTokenAsync(int guardianId, CancellationToken ct = default) =>
        _db.Guardians.Where(g => g.GuardianId == guardianId).Select(g => g.FcmToken).FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
