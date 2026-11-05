namespace ChildGuard.Application.Common;

/// <summary>RFID scan handling, bound from the "Rfid" configuration section.</summary>
public class RfidOptions
{
    /// <summary>
    /// A repeat of the student's latest checkpoint within this many seconds is answered
    /// "Already recorded" (success, nothing new written) instead of a rejection, so a
    /// double tap or a scanner retrying a lost response does not show as a failure.
    /// The next legitimate scan of the same checkpoint only follows a full journey
    /// cycle or a new school day, which is far longer than this window.
    /// </summary>
    public int DuplicateScanWindowSeconds { get; set; } = 120;

    public TimeSpan DuplicateScanWindow => TimeSpan.FromSeconds(Math.Max(0, DuplicateScanWindowSeconds));
}

/// <summary>Pickup verification handling, bound from the "Verification" configuration section.</summary>
public class VerificationOptions
{
    /// <summary>A matched verification not decided within this many minutes expires and cannot be approved.</summary>
    public int PendingExpiryMinutes { get; set; } = 15;

    public TimeSpan PendingExpiry => TimeSpan.FromMinutes(Math.Max(1, PendingExpiryMinutes));
}
