namespace ChildGuard.Domain.Entities;

/// <summary>
/// Append-only record of one change or important action. Plain id columns, no foreign keys: the trail must
/// never block, or be removed with, the records it describes. Never holds passwords, tokens, face images or
/// biometric data.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    /// <summary>Admin, Guardian, Device, System or Anonymous.</summary>
    public string ActorType { get; set; } = default!;
    public int? ActorId { get; set; }
    /// <summary>The actor's name when the action happened.</summary>
    public string? ActorName { get; set; }
    /// <summary>What happened, e.g. StudentCreated, GuardianLinked, RfidCardAssigned.</summary>
    public string Action { get; set; } = default!;
    /// <summary>Student, Guardian, RfidCard, RfidDevice, Admin, Enrollment, Transport, Import, Pickup.</summary>
    public string EntityType { get; set; } = default!;
    public int? EntityId { get; set; }
    /// <summary>The student this entry concerns, when there is one (for a student's history).</summary>
    public int? StudentId { get; set; }
    /// <summary>The guardian this entry concerns, when there is one (for a guardian's history).</summary>
    public int? GuardianId { get; set; }
    public string Summary { get; set; } = default!;
    /// <summary>JSON of the relevant previous and new values, redacted.</summary>
    public string? Details { get; set; }
    /// <summary>The reason an admin gave, for actions that require one.</summary>
    public string? Reason { get; set; }
}
