using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Guardian
{
    public int GuardianId { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = default!;
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    /// <summary>True while the Guardian is still on an admin-supplied default password. Blocks JWT issuance until a new password is chosen.</summary>
    public bool PasswordResetRequired { get; set; }
    /// <summary>Firebase Cloud Messaging device token, registered by the Guardian app after login. Null until the app registers one.</summary>
    public string? FcmToken { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudentGuardian> StudentGuardians { get; set; } = new List<StudentGuardian>();
    public GuardianFaceData? FaceData { get; set; }
    /// <summary>Additional reference faces; the first face lives in <see cref="FaceData"/>.</summary>
    public ICollection<GuardianFaceReference> FaceReferences { get; set; } = new List<GuardianFaceReference>();
    /// <summary>Profile image, when one has been uploaded. Never loaded unless explicitly included.</summary>
    public GuardianPhoto? Photo { get; set; }
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<GuardianOtpChallenge> OtpChallenges { get; set; } = new List<GuardianOtpChallenge>();
}
