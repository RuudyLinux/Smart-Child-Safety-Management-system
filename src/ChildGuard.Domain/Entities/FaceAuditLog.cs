using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// Append-only record of one face-verification layer decision or biometric-data access.
/// Plain id columns, no foreign keys: the trail must outlive and never block changes to
/// the records it describes. Detail carries codes and scores only, never images,
/// embeddings or tokens.
/// </summary>
public class FaceAuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string ActorRole { get; set; } = default!;
    public int? ActorId { get; set; }
    public FaceAuditLayer Layer { get; set; }
    public string Action { get; set; } = default!;
    public string Outcome { get; set; } = default!;
    public int? StudentId { get; set; }
    public int? GuardianId { get; set; }
    public int? VerificationId { get; set; }
    public int? SessionId { get; set; }
    public string? Detail { get; set; }
}
