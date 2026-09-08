using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IGuardianService
{
    Task<List<GuardianListItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result<GuardianDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int id, UpdateGuardianStatusRequest request, CancellationToken ct = default);
    Task<Result> UpdateProfileAsync(int id, UpdateProfileRequest request, CancellationToken ct = default);
    Task<Result> RegisterDeviceTokenAsync(int id, string fcmToken, CancellationToken ct = default);
}
