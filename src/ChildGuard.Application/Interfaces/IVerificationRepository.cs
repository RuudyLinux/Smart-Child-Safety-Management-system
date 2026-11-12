using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface IVerificationRepository
{
    Task<GuardianFaceData?> GetFaceDataAsync(int guardianId, CancellationToken ct = default);
    Task UpsertFaceDataAsync(GuardianFaceData data, CancellationToken ct = default);

    /// <summary>Active additional reference faces, oldest first. The primary face is <see cref="GetFaceDataAsync"/>.</summary>
    Task<List<GuardianFaceReference>> GetActiveReferencesAsync(int guardianId, CancellationToken ct = default);
    Task<GuardianFaceReference?> GetReferenceAsync(int guardianId, int referenceId, CancellationToken ct = default);
    Task AddReferenceAsync(GuardianFaceReference reference, CancellationToken ct = default);

    Task AddSessionAsync(VerificationSession session, CancellationToken ct = default);
    Task<VerificationSession?> GetSessionAsync(int id, CancellationToken ct = default);
    /// <summary>True if a live capture with this neutral-frame hash was already used (a replayed capture).</summary>
    Task<bool> IsNeutralFrameHashUsedAsync(string hash, CancellationToken ct = default);
    /// <summary>Closes the admin's other open sessions for this student, so only the newest one is live.</summary>
    Task CloseOpenSessionsAsync(int studentId, int adminId, CancellationToken ct = default);

    Task AddVerificationAsync(GuardianVerification verification, CancellationToken ct = default);
    Task<GuardianVerification?> GetVerificationByIdAsync(int id, CancellationToken ct = default);
    Task<List<GuardianVerification>> GetHistoryAsync(int? studentId, DateTime? fromUtc = null, DateTime? toUtcExclusive = null, CancellationToken ct = default);

    /// <summary>Most recent verification attempts made by one guardian, newest first.</summary>
    Task<List<GuardianVerification>> GetHistoryForGuardianAsync(int guardianId, int take, CancellationToken ct = default);

    /// <summary>
    /// Guardian id to their latest verification attempt. Backs the "last activity"
    /// column without loading every verification row into the guardian list.
    /// </summary>
    Task<Dictionary<int, DateTime>> GetLastActivityByGuardianAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
