using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Admin
{
    public int AdminId { get; set; }
    public string FullName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
