namespace ChildGuard.Domain.Entities;

/// <summary>
/// Stores the guardian's face embedding (numeric vector), not raw images,
/// per the "secure representation over raw image storage" requirement.
/// </summary>
public class GuardianFaceData
{
    public int Id { get; set; }
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public string EmbeddingJson { get; set; } = default!;
    public string Model { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
