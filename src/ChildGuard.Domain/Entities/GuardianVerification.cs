using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class GuardianVerification
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    public double MatchScore { get; set; }
    public VerificationResult Result { get; set; }
    public bool PickupApproved { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public Admin? ApprovedByAdmin { get; set; }
    public string? Notes { get; set; }

    // ---- Verification layers (face verification with liveness). All null on records made before
    // layers existed and by the original face-verify endpoint; those read as "legacy, no liveness".
    // For a layered record Result is Match only when every mandatory layer passed.
    public CaptureSource? CaptureSource { get; set; }
    public CheckResult? QualityResult { get; set; }
    public CheckResult? FaceDetectionResult { get; set; }
    public IdentityResult? IdentityResult { get; set; }
    public AuthorisationResult? AuthorisationResult { get; set; }
    public LivenessResult? LivenessResult { get; set; }
    /// <summary>Mean passive anti-spoofing P(real) over the sampled frames.</summary>
    public double? LivenessScore { get; set; }
    /// <summary>The reference face that matched best: 0 is the primary face, otherwise a GuardianFaceReference id.</summary>
    public int? BestReferenceId { get; set; }
    public int? SessionId { get; set; }
    /// <summary>Reason codes for the outcome (never biometric data), e.g. "PASSIVE_SPOOF_SUSPECTED".</summary>
    public string? LayerNotes { get; set; }

    /// <summary>
    /// App-managed optimistic concurrency token — bumped by the service before
    /// every save, so two admins approving/rejecting the same pickup at once
    /// can't both win the check-then-act in ApproveAsync/RejectAsync.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// The verification's state at <paramref name="nowUtc"/>. Expiry is measured
    /// between two UTC instants, so it does not depend on server time zone or
    /// restarts. A decision (approve/reject) is final and is never reported as expired.
    /// </summary>
    public PickupVerificationState GetState(DateTime nowUtc, TimeSpan pendingExpiry)
    {
        if (PickupApproved) return PickupVerificationState.Approved;
        if (ApprovedByAdminId is not null) return PickupVerificationState.Rejected;
        if (Result != VerificationResult.Match) return PickupVerificationState.NotMatched;
        return nowUtc - AttemptedAt > pendingExpiry
            ? PickupVerificationState.Expired
            : PickupVerificationState.Pending;
    }
}
