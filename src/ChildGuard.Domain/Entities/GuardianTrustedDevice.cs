namespace ChildGuard.Domain.Entities;

/// <summary>
/// A phone that has already passed the emailed OTP for this guardian. While it is trusted the guardian signs in
/// with the password alone from that phone; any other phone still needs the OTP. Only a SHA-256 of the device
/// secret is stored, so a database leak cannot be replayed. Trust is cleared (RevokedAt, never deleted) whenever
/// the password changes or an admin resets it, and lapses after a period of disuse.
/// </summary>
public class GuardianTrustedDevice
{
    public int Id { get; set; }
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;

    /// <summary>Lower-case hex SHA-256 of the 256-bit secret held by the phone.</summary>
    public string TokenHash { get; set; } = default!;

    /// <summary>Phone description supplied by the app (e.g. "Android"). Display only.</summary>
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }

    /// <summary>Slides forward on every use; the device stops being trusted once it passes.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && ExpiresAt > nowUtc;
}
