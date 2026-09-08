using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IVerificationRepository
{
    Task<GuardianFaceData?> GetFaceDataAsync(int guardianId, CancellationToken ct = default);
    Task UpsertFaceDataAsync(GuardianFaceData data, CancellationToken ct = default);

    Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default);
    Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default);
    Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
