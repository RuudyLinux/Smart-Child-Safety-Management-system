using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IGuardianRepository
{
    Task<Guardian?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Guardian?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Guardian?> GetWithStudentsAsync(int id, CancellationToken ct = default);
    Task<List<Guardian>> GetAllAsync(CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task AddAsync(Guardian guardian, CancellationToken ct = default);
    Task<string?> GetFcmTokenAsync(int guardianId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
