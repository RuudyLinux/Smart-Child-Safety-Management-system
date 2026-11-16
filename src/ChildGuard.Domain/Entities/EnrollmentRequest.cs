namespace ChildGuard.Domain.Entities;

/// <summary>
/// Idempotency and audit record for one completed enrollment. Written in the enrollment's own transaction,
/// so a resubmitted or concurrent request with the same key finds the student instead of creating a second.
/// </summary>
public class EnrollmentRequest
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = default!;
    /// <summary>SHA-256 (hex) of the request without its key. A key reused with different content is refused.</summary>
    public string RequestHash { get; set; } = default!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int? AdminId { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>The admin's reason for enrolling despite a student with the same name and date of birth. Null when none matched.</summary>
    public string? DuplicateOverrideReason { get; set; }
    /// <summary>Comma-separated ids of deactivated guardian accounts the admin confirmed linking. Null when none.</summary>
    public string? ConfirmedInactiveGuardianIds { get; set; }
}
