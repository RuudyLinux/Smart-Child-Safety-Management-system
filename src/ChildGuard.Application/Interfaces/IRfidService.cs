using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IRfidService
{
    Task<List<RfidCardListItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result> AssignAsync(AssignRfidRequest request, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int cardId, UpdateRfidStatusRequest request, CancellationToken ct = default);
    Task<List<RfidDeviceDto>> GetDevicesAsync(CancellationToken ct = default);
    Task<Result> HeartbeatAsync(int deviceId, CancellationToken ct = default);
    Task<RfidScanResultDto> ProcessScanAsync(RfidScanRequest request, CancellationToken ct = default);
}
