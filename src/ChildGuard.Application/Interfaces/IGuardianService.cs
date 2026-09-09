using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IGuardianService
{
    Task<List<GuardianListItemDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a Guardian on the fixed initial password, flags the account for the
    /// mandatory first-login reset, and emails the credentials to the Guardian's
    /// own registered address.
    /// </summary>
    Task<Result<CreateGuardianResultDto>> CreateAsync(CreateGuardianRequest request, CancellationToken ct = default);
    Task<Result<GuardianDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    /// <summary>Admin edit of name, email, contact number and status in one operation.</summary>
    Task<Result> AdminUpdateAsync(int id, AdminUpdateGuardianRequest request, CancellationToken ct = default);

    Task<Result> UpdateStatusAsync(int id, UpdateGuardianStatusRequest request, CancellationToken ct = default);
    Task<Result> UpdateProfileAsync(int id, UpdateProfileRequest request, CancellationToken ct = default);
    Task<Result> RegisterDeviceTokenAsync(int id, string fcmToken, CancellationToken ct = default);
    Task<Result> AdminUpdateCredentialsAsync(int id, AdminUpdateGuardianCredentialsRequest request, CancellationToken ct = default);
}
