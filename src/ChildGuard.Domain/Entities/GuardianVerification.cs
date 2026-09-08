using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class GuardianVerification
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int GuardianId { get; set; }
    public Guardian Guardian { get; set; } = default!;
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    public double MatchScore { get; set; }
    public VerificationResult Result { get; set; }
    public bool PickupApproved { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public Admin? ApprovedByAdmin { get; set; }
    public string? Notes { get; set; }
}
