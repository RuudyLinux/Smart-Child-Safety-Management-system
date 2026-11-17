using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IVerificationService
{
    Task<Result> RegisterFaceAsync(int guardianId, RegisterFaceRequest request, CancellationToken ct = default);
    Task<Result<FaceVerifyResultDto>> VerifyAsync(FaceVerifyRequest request, CancellationToken ct = default);
    Task<Result<PickupDecisionResultDto>> ApproveAsync(ApprovePickupRequest request, int approvingAdminId, CancellationToken ct = default);
    Task<Result<PickupDecisionResultDto>> RejectAsync(RejectPickupRequest request, int actingAdminId, CancellationToken ct = default);
    /// <summary>Verification attempts, newest first; optional inclusive UTC date bounds on AttemptedAt.</summary>
    Task<List<GuardianVerificationHistoryDto>> GetHistoryAsync(int? studentId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
    Task<Paged<GuardianVerificationHistoryDto>> GetHistoryPageAsync(VerificationHistoryFilter filter, PageRequest page, CancellationToken ct = default);
    Task<VerificationHistorySummaryDto> GetHistorySummaryAsync(int? studentId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
}
