using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record RegisterFaceRequest(string ImageBase64);

public record FaceVerifyRequest(int StudentId, int GuardianId, string ImageBase64);

/// <param name="ExpiresAt">UTC instant after which a matched verification can no longer be approved.</param>
/// <param name="FaceRegistered">False when the guardian has no registered face, so no comparison ran
/// (not the same as "did not match"). Added 2026-11-07; older clients ignore it.</param>
public record FaceVerifyResultDto(
    int VerificationId, bool GuardianAuthorized, bool FaceMatched, double MatchScore,
    string Message, DateTime? ExpiresAt = null, bool FaceRegistered = true);

public record ApprovePickupRequest(int VerificationId);
public record RejectPickupRequest(int VerificationId, string? Reason);

public record PickupDecisionResultDto(bool Approved, string Message, JourneyEventType? RecordedEventType);

public record GuardianVerificationHistoryDto(
    int Id, int StudentId, string StudentName, int GuardianId, string GuardianName,
    DateTime AttemptedAt, double MatchScore, VerificationResult Result, bool PickupApproved,
    PickupVerificationState State = PickupVerificationState.Pending, DateTime? ExpiresAt = null,
    FaceLayersDto? Layers = null);
