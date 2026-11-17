using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// A one-off permission for someone who is not a registered guardian (a relative, a neighbour) to collect
/// one student inside a fixed window of at most 24 hours. Requested by a pickup-authorised guardian of that
/// student, approved by an admin, and used at most once: the handover needs the QR token issued on approval
/// and a face match against the photo supplied with the request.
///
/// Deliberately separate from <see cref="Guardian"/> and <see cref="StudentGuardian"/>: a temporary person
/// gets no account, no login and no standing link to the student, so nothing outlives the window.
/// History table: never deleted; a request ends as Rejected, Cancelled, Revoked, Completed or (derived) Expired.
/// </summary>
public class TemporaryPickupAuthorization
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    /// <summary>The guardian who asked for it; only they may cancel it.</summary>
    public int RequestedByGuardianId { get; set; }
    public Guardian RequestedByGuardian { get; set; } = default!;

    // ---- The temporary person ----
    public string FullName { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string Relationship { get; set; } = default!;
    /// <summary>Optional ID hint the desk can check, e.g. "Driving licence, last 4: 1234". Never a full ID number.</summary>
    public string? IdentityNote { get; set; }
    public string? Notes { get; set; }

    // ---- Window (UTC instants) ----
    public DateTime ValidFromUtc { get; set; }
    public DateTime ValidUntilUtc { get; set; }

    public TemporaryPickupStatus Status { get; set; } = TemporaryPickupStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- Admin decision (approve, reject or revoke) ----
    public int? DecidedByAdminId { get; set; }
    public Admin? DecidedByAdmin { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTime? CancelledAt { get; set; }

    // ---- Handover ----
    public DateTime? CompletedAt { get; set; }
    public int? CompletedByAdminId { get; set; }
    public Admin? CompletedByAdmin { get; set; }
    public double? HandoverFaceScore { get; set; }
    public int? JourneyEventId { get; set; }

    /// <summary>Random value the QR token is derived from. Set on approval; cleared token = no valid QR.</summary>
    public string? QrNonce { get; set; }

    /// <summary>Face embedding of the supplied photo, protected like guardian embeddings. Never sent to clients.</summary>
    public string FaceEmbeddingJson { get; set; } = default!;
    public string? FaceModel { get; set; }

    public TemporaryPickupPhoto? Photo { get; set; }

    /// <summary>Optimistic concurrency token, bumped by the service before every state change.</summary>
    public uint Version { get; set; }

    /// <summary>
    /// The effective state at <paramref name="nowUtc"/>. Terminal statuses are reported as stored; an
    /// undecided or approved request whose window has closed reads as Expired.
    /// </summary>
    public TemporaryPickupState GetState(DateTime nowUtc) => Status switch
    {
        TemporaryPickupStatus.Completed => TemporaryPickupState.Completed,
        TemporaryPickupStatus.Rejected => TemporaryPickupState.Rejected,
        TemporaryPickupStatus.Cancelled => TemporaryPickupState.Cancelled,
        TemporaryPickupStatus.Revoked => TemporaryPickupState.Revoked,
        _ when nowUtc >= ValidUntilUtc => TemporaryPickupState.Expired,
        TemporaryPickupStatus.Pending => TemporaryPickupState.PendingApproval,
        TemporaryPickupStatus.Approved when nowUtc < ValidFromUtc => TemporaryPickupState.Scheduled,
        TemporaryPickupStatus.Approved => TemporaryPickupState.Active,
        _ => TemporaryPickupState.Expired
    };

    /// <summary>The embedding protector authenticates an owner id; temporary people use the negated id so they
    /// can never collide with a guardian's.</summary>
    public int ProtectionOwnerId => -Id;
}

/// <summary>The photo supplied with the request. Shown to the admin and the desk; bytes never leave the API otherwise.</summary>
public class TemporaryPickupPhoto : ProfilePhoto
{
    public int TemporaryPickupAuthorizationId { get; set; }
    public TemporaryPickupAuthorization Authorization { get; set; } = default!;
}

/// <summary>
/// Append-only trail of everything that happened to a temporary pickup: request, decisions, QR scans,
/// face checks, handover, refusals. Plain id columns (no foreign keys) so the trail never blocks or is
/// removed with the records it describes. Detail holds codes and scores only — never images or tokens.
/// </summary>
public class TemporaryPickupAuditEntry
{
    public long Id { get; set; }
    public int AuthorizationId { get; set; }
    public int StudentId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Action { get; set; } = default!;
    public string ActorRole { get; set; } = default!;
    public int? ActorId { get; set; }
    public string? Detail { get; set; }
}
