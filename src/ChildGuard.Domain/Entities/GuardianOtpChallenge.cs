namespace ChildGuard.Domain.Entities;

/// <summary>
/// A one-time-password challenge raised after a Guardian's credentials validate.
/// The Guardian is not authenticated until the challenge is verified, so this row
/// — not a client-side flag — is what gates JWT issuance.
/// Codes are stored hashed with the same hasher used for passwords; the plaintext
/// only ever exists in memory and in the SMS sent to the registered contact number.
/// </summary>
public class GuardianOtpChallenge
{
    public int GuardianOtpChallengeId { get; set; }

    /// <summary>Opaque handle handed to the client so the OTP step is bound to this login attempt.</summary>
    public Guid ChallengeToken { get; set; } = Guid.NewGuid();

    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;

    public string CodeHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Failed verification attempts against the current code. Reset when a new code is sent.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>How many times a fresh code has been re-sent for this challenge.</summary>
    public int ResendCount { get; set; }

    public DateTime LastSentAtUtc { get; set; }

    /// <summary>Set once the correct code is supplied. Null while the challenge is still open.</summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>True once the challenge can no longer be used — verified, superseded, or attempt-limit reached.</summary>
    public bool IsConsumed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
