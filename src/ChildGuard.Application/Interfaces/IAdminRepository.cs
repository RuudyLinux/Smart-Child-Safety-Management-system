using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IAdminRepository
{
    Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Admin?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(Admin admin, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
