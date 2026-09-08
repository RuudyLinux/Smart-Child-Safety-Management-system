using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IVerificationService
{
    Task<Result> RegisterFaceAsync(int guardianId, RegisterFaceRequest request, CancellationToken ct = default);
    Task<Result<FaceVerifyResultDto>> VerifyAsync(FaceVerifyRequest request, CancellationToken ct = default);
    Task<Result<PickupDecisionResultDto>> ApproveAsync(ApprovePickupRequest request, int approvingAdminId, CancellationToken ct = default);
    Task<Result<PickupDecisionResultDto>> RejectAsync(RejectPickupRequest request, int actingAdminId, CancellationToken ct = default);
    Task<List<GuardianVerificationHistoryDto>> GetHistoryAsync(int? studentId, CancellationToken ct = default);
}
