using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class AdminRepository : IAdminRepository
{
    private readonly ChildGuardDbContext _db;
    public AdminRepository(ChildGuardDbContext db) => _db = db;

    public Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Admins.FirstOrDefaultAsync(a => a.Email == email, ct);

    public Task<Admin?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Admins.FirstOrDefaultAsync(a => a.AdminId == id, ct);

    public async Task AddAsync(Admin admin, CancellationToken ct = default) =>
        await _db.Admins.AddAsync(admin, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
