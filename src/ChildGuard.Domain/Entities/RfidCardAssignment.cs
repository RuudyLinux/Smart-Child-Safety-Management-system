namespace ChildGuard.Domain.Entities;

/// <summary>
/// One period during which a card belonged to a student. Append-only history: the card's
/// current link stays on <see cref="RfidCard.StudentId"/>; this table records how it got there.
/// </summary>
public class RfidCardAssignment
{
    public long Id { get; set; }
    public int RfidCardId { get; set; }
    public RfidCard RfidCard { get; set; } = default!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    /// <summary>When the card was given to the student. Null on a migrated snapshot whose card had no recorded date.</summary>
    public DateTime? AssignedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>When this history row was written.</summary>
    public DateTime RecordedAt { get; set; }
    public int? AssignedByAdminId { get; set; }
    /// <summary>
    /// True for rows created by the migration from links that existed before history was kept.
    /// Their AssignedAt is copied from the card as it was, never estimated.
    /// </summary>
    public bool IsMigratedSnapshot { get; set; }
}
