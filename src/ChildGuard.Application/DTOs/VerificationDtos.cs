using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record RegisterFaceRequest(string ImageBase64);

public record FaceVerifyRequest(int StudentId, int GuardianId, string ImageBase64);

public record FaceVerifyResultDto(
    int VerificationId, bool GuardianAuthorized, bool FaceMatched, double MatchScore,
    string Message);

public record ApprovePickupRequest(int VerificationId);
public record RejectPickupRequest(int VerificationId, string? Reason);

public record PickupDecisionResultDto(bool Approved, string Message, JourneyEventType? RecordedEventType);

public record GuardianVerificationHistoryDto(
    int Id, int StudentId, string StudentName, int GuardianId, string GuardianName,
    DateTime AttemptedAt, double MatchScore, VerificationResult Result, bool PickupApproved);
