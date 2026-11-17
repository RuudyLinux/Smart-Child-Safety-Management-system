using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record GuardianListItemDto(
    int GuardianId, string FullName, string Email, string? Phone, RecordStatus Status, int LinkedStudentCount,
    bool HasPhoto, DateTime? PhotoUpdatedAt, bool FaceRegistered, bool PasswordResetRequired, DateTime? LastActivityAt,
    DateTime? ArchivedAt = null);

public record GuardianDetailDto(
    int GuardianId, string FullName, string Email, string? Phone, RecordStatus Status,
    bool FaceRegistered, bool PasswordResetRequired, IReadOnlyList<StudentSummaryDto> Students, DateTime CreatedAt,
    bool HasPhoto, DateTime? PhotoUpdatedAt, DateTime? LastActivityAt,
    IReadOnlyList<GuardianVerificationHistoryDto> RecentVerifications,
    AddressDto? Address = null,
    DateTime? ArchivedAt = null, string? ArchiveReason = null);

/// <summary>
/// A child as seen from their guardian — identity, relationship, and the live
/// safety picture the Guardian app and the admin's guardian profile both show.
/// </summary>
public record StudentSummaryDto(
    int StudentId, string FullName, string? Grade, string? Relationship, bool IsAuthorizedForPickup,
    bool HasPhoto, StudentSafetyStatus SafetyStatus, DateTime? LastEventAt, JourneyEventType? LastEventType,
    TimeOnly? CheckInTime, bool PresentToday, string? BusNumber, string? RouteName);

public record UpdateGuardianStatusRequest(RecordStatus Status);

public record UpdateProfileRequest(string FullName, string? Phone);

public record RegisterDeviceTokenRequest(string FcmToken);

/// <summary>
/// Admin edit of a Guardian's own details. Email and contact number are both login-
/// relevant, so each is checked for uniqueness before it is applied.
/// </summary>
public record AdminUpdateGuardianRequest(string FullName, string Email, string ContactNumber, RecordStatus Status);

/// <summary>
/// Admin-issued password. Separate from <see cref="AdminUpdateGuardianRequest"/> because
/// issuing a password is a distinct, sensitive action: it always flags the account for the
/// forced first-login reset.
/// </summary>
public record AdminUpdateGuardianCredentialsRequest(string NewPassword);

/// <summary>Admin-initiated Guardian creation. The initial password is set server-side, never supplied by the client.</summary>
public record CreateGuardianRequest(string FullName, string Email, string ContactNumber);

/// <summary>
/// Outcome of creating a Guardian. <paramref name="CredentialEmailSent"/> is false when the
/// account was created but the credential email could not be delivered, so the admin is told
/// rather than the failure being swallowed.
/// </summary>
public record CreateGuardianResultDto(int GuardianId, bool CredentialEmailSent, string? EmailError);

/// <summary>School-wide guardian figures for the Guardians page header, in one call (same meaning as the list filters).</summary>
public record GuardianListSummaryDto(int Total, int FaceRegistered, int WithPhoto, int AwaitingFirstSignIn, int WithoutStudents);
