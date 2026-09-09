using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record RfidCardListItemDto(
    int RfidCardId, string MaskedCardUid, RfidCardStatus Status,
    int? StudentId, string? StudentName, DateTime? AssignedAt);

public record RfidCardDetailDto(
    int RfidCardId, string MaskedCardUid, RfidCardStatus Status,
    int? StudentId, string? StudentName, string? StudentGrade, DateTime? AssignedAt, DateTime CreatedAt);

public record AssignRfidRequest(string CardUid, int StudentId);

public record UpdateRfidStatusRequest(RfidCardStatus Status);

public record RfidDeviceDto(int RfidDeviceId, string DeviceLabel, RfidConnectionStatus ConnectionStatus, DateTime? LastSeenAt);

public record RfidScanRequest(string CardUid, int RfidDeviceId, JourneyEventType Checkpoint);

public record RfidScanResultDto(
    bool StudentIdentified,
    int? StudentId,
    string? StudentName,
    string? MaskedCardUid,
    bool EventRecorded,
    JourneyEventType? RecordedEventType,
    string Message);
