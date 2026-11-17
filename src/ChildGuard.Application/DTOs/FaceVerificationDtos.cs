using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record StartVerificationSessionRequest(int StudentId);

/// <summary>A guardian the desk may verify for this student. Never carries an embedding or any other guardian's data.</summary>
public record SessionGuardianDto(int GuardianId, string FullName, string? Relationship, bool FaceRegistered, int ReferenceCount);

public record VerificationSessionDto(
    int SessionId, int StudentId, string StudentName, DateTime ExpiresAt, int MaxLiveAttempts,
    IReadOnlyList<SessionGuardianDto> Guardians);

/// <param name="Source">Webcam or Gallery. Either can identify; neither of these calls can establish liveness.</param>
public record SessionIdentifyRequest(CaptureSource Source, string ImageBase64);

/// <param name="ChallengeToken">Single use, bound to this session and its admin, valid until <paramref name="ExpiresAt"/>.</param>
/// <param name="Steps">Head movements to ask for, in order: "turn_left" / "turn_right" (the person's own left and right).</param>
public record LiveChallengeDto(
    string ChallengeToken, IReadOnlyList<string> Steps, DateTime ExpiresAt, int MinFrames, int MaxFrames, double MinSeconds);

public record LiveFrameDto(string ImageBase64, int TMs);
public record LiveVerifyRequest(string ChallengeToken, IReadOnlyList<LiveFrameDto> Frames);

public record MatchedGuardianDto(int GuardianId, string FullName, string? Relationship);

/// <summary>The independent results of each verification layer. Null means the layer was not evaluated.</summary>
public record FaceLayersDto(
    CheckResult? Quality, CheckResult? FaceDetection, IdentityResult? Identity, AuthorisationResult? Authorisation,
    LivenessResult? Liveness, CaptureSource? Source, double? LivenessScore, int? BestReferenceId, string? ReasonCodes)
{
    /// <summary>Null for a record made before layers existed (or by the original face-verify endpoint): "legacy, no liveness".</summary>
    public static FaceLayersDto? From(ChildGuard.Domain.Entities.GuardianVerification v) =>
        v.CaptureSource is null && v.QualityResult is null && v.IdentityResult is null && v.LivenessResult is null
            ? null
            : new FaceLayersDto(v.QualityResult, v.FaceDetectionResult, v.IdentityResult, v.AuthorisationResult,
                v.LivenessResult, v.CaptureSource, v.LivenessScore, v.BestReferenceId, v.LayerNotes);
}

/// <summary>
/// Outcome of one desk attempt. <c>Verified</c> is true only when every mandatory layer passed, and even then
/// the pickup is only pending: an administrator still approves or rejects it. Do not show a "verified" state
/// from anything else.
/// </summary>
public record VerificationAttemptResultDto(
    string Outcome, bool Verified, string NextStep, string Message,
    CheckResult Quality, CheckResult FaceDetection, IdentityResult? Identity, AuthorisationResult Authorisation, LivenessResult Liveness,
    MatchedGuardianDto? Guardian, double? Score, double Threshold, string? ConfidenceBand, string? ReferenceLabel,
    IReadOnlyList<string> Issues, IReadOnlyList<string> Warnings, IReadOnlyList<string> ReasonCodes,
    int? VerificationId, DateTime? ExpiresAt, int AttemptsRemaining);
