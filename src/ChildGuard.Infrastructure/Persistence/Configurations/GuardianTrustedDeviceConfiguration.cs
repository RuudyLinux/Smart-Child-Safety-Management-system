using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianTrustedDeviceConfiguration : IEntityTypeConfiguration<GuardianTrustedDevice>
{
    public void Configure(EntityTypeBuilder<GuardianTrustedDevice> b)
    {
        b.ToTable("GuardianTrustedDevices");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(x => x.Label).HasMaxLength(100);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.GuardianId, x.RevokedAt });

        // Guardians are deactivated, never deleted, so RESTRICT like the other history tables.
        b.HasOne(x => x.Guardian).WithMany().HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
    }
}
