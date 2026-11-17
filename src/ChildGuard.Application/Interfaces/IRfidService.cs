using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

public interface IRfidService
{
    Task<List<RfidCardListItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<Result<RfidCardDetailDto>> GetByIdAsync(int cardId, CancellationToken ct = default);
    Task<Result> AssignAsync(AssignRfidRequest request, CancellationToken ct = default);
    // ---- inventory: every operation runs under a row lock on the cards involved and is audited

    Task<Result<RfidOperationResultDto>> RegisterAsync(RegisterRfidCardRequest request, int adminId, CancellationToken ct = default);
    Task<Result<RfidOperationResultDto>> AssignRegisteredAsync(int cardId, AssignRegisteredCardRequest request, int adminId, CancellationToken ct = default);
    Task<Result<RfidOperationResultDto>> UnassignAsync(int cardId, UnassignRfidRequest request, int adminId, CancellationToken ct = default);
    Task<Result<RfidOperationResultDto>> ReplaceAsync(int cardId, ReplaceRfidRequest request, int adminId, CancellationToken ct = default);
    Task<Result<RfidOperationResultDto>> ReassignAsync(int cardId, ReassignRfidRequest request, int adminId, CancellationToken ct = default);
    /// <summary>Permanent removal, only for a card that was never assigned and never recorded anything.</summary>
    Task<Result<RfidOperationResultDto>> DeleteAsync(int cardId, string reason, int adminId, CancellationToken ct = default);

    /// <summary>Whether this UID could be given to a student right now. Read-only.</summary>
    Task<RfidCardCheckDto> CheckCardAsync(string cardUid, CancellationToken ct = default);
    Task<Result> UpdateStatusAsync(int cardId, UpdateRfidStatusRequest request, CancellationToken ct = default);
    Task<List<RfidDeviceDto>> GetDevicesAsync(CancellationToken ct = default);
    Task<Result<RfidDeviceDto>> CreateDeviceAsync(CreateRfidDeviceRequest request, CancellationToken ct = default);
    Task<Result> RenameDeviceAsync(int deviceId, UpdateRfidDeviceRequest request, CancellationToken ct = default);
    Task<Result> HeartbeatAsync(int deviceId, CancellationToken ct = default);
    Task<RfidScanResultDto> ProcessScanAsync(RfidScanRequest request, CancellationToken ct = default);

    /// <summary>Looks up the active student on a card. Records no journey event or attendance.</summary>
    Task<RfidIdentifyResultDto> IdentifyAsync(RfidIdentifyRequest request, CancellationToken ct = default);

    /// <summary>Issues (or rotates) this scanner's own API key. The plaintext is returned once and never stored.</summary>
    Task<Result<DeviceCredentialDto>> IssueDeviceKeyAsync(int deviceId, CancellationToken ct = default);

    /// <summary>Revokes this scanner's API key; it can no longer authenticate.</summary>
    Task<Result> RevokeDeviceKeyAsync(int deviceId, CancellationToken ct = default);
}
