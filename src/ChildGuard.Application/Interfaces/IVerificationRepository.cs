using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IVerificationRepository
{
    Task<GuardianFaceData?> GetFaceDataAsync(int guardianId, CancellationToken ct = default);
    Task UpsertFaceDataAsync(GuardianFaceData data, CancellationToken ct = default);

    Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default);
    Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default);
    Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, CancellationToken ct = default);

    /// <summary>Most recent verification attempts made by one guardian, newest first.</summary>
    Task<List<GuardianVerification>> GetHistoryForGuardianAsync(int guardianId, int take, CancellationToken ct = default);

    /// <summary>
    /// Guardian id to their latest verification attempt. Backs the "last activity"
    /// column without loading every verification row into the guardian list.
    /// </summary>
    Task<Dictionary<int, DateTime>> GetLastActivityByGuardianAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
