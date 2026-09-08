namespace ChildGuard.Domain.Entities;

/// <summary>
/// Many-to-many link between Student and Guardian, carrying pickup authorization.
/// Not an entity named explicitly in the source spec's entity list, but required to
/// correctly model "authorized guardian" (a per student-guardian pair flag).
/// </summary>
public class StudentGuardian
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public string? Relationship { get; set; }
    public bool IsAuthorizedForPickup { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
