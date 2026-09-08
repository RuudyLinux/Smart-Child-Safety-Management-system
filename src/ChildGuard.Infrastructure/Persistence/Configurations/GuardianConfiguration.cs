using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> b)
    {
        b.HasKey(x => x.GuardianId);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        b.Property(x => x.Email).IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();

        b.HasOne(x => x.FaceData)
            .WithOne(x => x.Guardian)
            .HasForeignKey<GuardianFaceData>(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
