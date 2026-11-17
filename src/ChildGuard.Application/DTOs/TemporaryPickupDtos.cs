using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <summary>
/// A guardian's request for a temporary pickup person. The window is UTC; it must be at most 24 hours long.
/// <paramref name="PhotoBase64"/> is a clear, front-facing photo of the person (JPEG, PNG or WebP) and is mandatory.
/// </summary>
public record CreateTemporaryPickupRequest(
    int StudentId,
    string FullName,
    string Phone,
    string Relationship,
    DateTime ValidFromUtc,
    DateTime ValidUntilUtc,
    string PhotoBase64,
    string? IdentityNote = null,
    string? Notes = null);

/// <summary>Reason for a reject, revoke or cancel. Optional for cancel, required for reject and revoke.</summary>
public record TemporaryPickupDecisionRequest(string? Reason);

/// <summary>A temporary pickup as listed to guardians and admins. Carries no biometric data and no QR token.</summary>
public record TemporaryPickupDto(
    int Id,
    int StudentId,
    string StudentName,
    string? StudentGrade,
    int RequestedByGuardianId,
    string RequestedByName,
    bool RequestedByYou,
    string FullName,
    string Phone,
    string Relationship,
    string? IdentityNote,
    string? Notes,
    DateTime ValidFromUtc,
    DateTime ValidUntilUtc,
    TemporaryPickupStatus Status,
    TemporaryPickupState State,
    DateTime CreatedAt,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? DecisionNote,
    DateTime? CompletedAt,
    bool HasPhoto,
    bool CanCancel,
    bool QrAvailable);

public record TemporaryPickupAuditDto(DateTime OccurredAt, string Action, string ActorRole, int? ActorId, string? Detail);

public record TemporaryPickupDetailDto(TemporaryPickupDto Pickup, IReadOnlyList<TemporaryPickupAuditDto> Audit);

/// <summary>The QR payload for an approved pickup. Shown by the guardian's app; scanned at the school desk.</summary>
public record TemporaryPickupQrDto(int Id, string Token, DateTime ValidFromUtc, DateTime ValidUntilUtc);

public record ScanTemporaryPickupRequest(string Token);

/// <summary>
/// What the desk sees after scanning a QR: who the person should be, which child, and whether a handover is
/// possible right now. <paramref name="BlockedReason"/> explains a refusal in plain words.
/// </summary>
public record TemporaryPickupScanResultDto(
    TemporaryPickupDto Pickup,
    bool CanHandOver,
    string? BlockedReason,
    StudentSafetyStatus StudentStatus);

/// <summary>Handover at the desk: the scanned token again plus a live photo of the person for the face check.</summary>
public record TemporaryPickupHandoverRequest(string Token, string ImageBase64);

public record TemporaryPickupHandoverResultDto(bool HandedOver, bool FaceMatched, double FaceScore, string Message);

/// <summary>Counts for the admin list header.</summary>
public record TemporaryPickupSummaryDto(int PendingApproval, int ActiveNow, int Scheduled, int CompletedToday);
