using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IRfidService
{
    Task<List<RfidCardListItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result<RfidCardDetailDto>> GetByIdAsync(int cardId, CancellationToken ct = default);
    Task<Result> AssignAsync(AssignRfidRequest request, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int cardId, UpdateRfidStatusRequest request, CancellationToken ct = default);
    Task<List<RfidDeviceDto>> GetDevicesAsync(CancellationToken ct = default);
    Task<Result> HeartbeatAsync(int deviceId, CancellationToken ct = default);
    Task<RfidScanResultDto> ProcessScanAsync(RfidScanRequest request, CancellationToken ct = default);

    /// <summary>Issues (or rotates) this scanner's own API key. The plaintext is returned once and never stored.</summary>
    Task<Result<DeviceCredentialDto>> IssueDeviceKeyAsync(int deviceId, CancellationToken ct = default);

    /// <summary>Revokes this scanner's API key; it can no longer authenticate.</summary>
    Task<Result> RevokeDeviceKeyAsync(int deviceId, CancellationToken ct = default);
}
