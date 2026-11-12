namespace ChildGuard.Domain.Entities;

/// <summary>
/// An additional reference face for a guardian (different light, angle or expression).
/// The guardian's first face stays in <see cref="GuardianFaceData"/>; this table holds the
/// extras, so existing data and every reader of the old table keep working unchanged.
/// Only the embedding is kept, never the photo. <see cref="EmbeddingJson"/> is either
/// plain JSON (no encryption key configured) or an "enc:v1:..." protected value.
/// Removing a reference only clears <see cref="IsActive"/>.
/// </summary>
public class GuardianFaceReference
{
    public int Id { get; set; }
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public string EmbeddingJson { get; set; } = default!;
    public string Model { get; set; } = default!;
    public string? Label { get; set; }
    /// <summary>Small JSON of the image-quality measurements at registration (no biometric data).</summary>
    public string? QualityJson { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedByRole { get; set; } = default!;
    public int? CreatedById { get; set; }
}
