using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// One desk verification for one student, owned by the admin who opened it. It binds the single-use
/// liveness challenge to that admin and student, remembers who a gallery photo identified (so the live
/// step has to match the same person), and expires if left incomplete. Only a hash of the challenge
/// token is stored, and no image is ever kept: the neutral-frame hash only stops the same capture
/// being submitted twice.
/// </summary>
public class VerificationSession
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int CreatedByAdminId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public VerificationSessionState State { get; set; } = VerificationSessionState.Open;

    /// <summary>Guardian a still image (gallery or webcam) identified and authorised; the live step must match the same guardian.</summary>
    public int? IdentifiedGuardianId { get; set; }
    public CaptureSource? IdentifiedSource { get; set; }
    public double? IdentifiedScore { get; set; }

    /// <summary>SHA-256 (hex) of the current challenge token. The token itself is never stored.</summary>
    public string? ChallengeTokenHash { get; set; }
    /// <summary>Comma-separated steps, e.g. "turn_left,turn_right".</summary>
    public string? ChallengeSteps { get; set; }
    public DateTime? ChallengeIssuedAt { get; set; }
    public DateTime? ChallengeExpiresAt { get; set; }
    public DateTime? ChallengeConsumedAt { get; set; }

    public int FailedLiveAttempts { get; set; }

    /// <summary>SHA-256 (hex) of the neutral frame of an accepted-for-analysis live capture; unique, so a captured burst cannot be replayed.</summary>
    public string? NeutralFrameHash { get; set; }

    public int? VerificationId { get; set; }

    /// <summary>App-managed concurrency token, bumped on every challenge issue and use, so two simultaneous submissions of one token cannot both win.</summary>
    public uint Version { get; set; }
}
