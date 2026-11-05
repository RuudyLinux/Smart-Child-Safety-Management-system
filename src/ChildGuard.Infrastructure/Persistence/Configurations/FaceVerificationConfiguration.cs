using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianFaceDataConfiguration : IEntityTypeConfiguration<GuardianFaceData>
{
    public void Configure(EntityTypeBuilder<GuardianFaceData> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.EmbeddingJson).IsRequired().HasColumnType("LONGTEXT");
        b.Property(x => x.Model).IsRequired().HasMaxLength(50);
        b.HasIndex(x => x.GuardianId).IsUnique();
    }
}

public class GuardianVerificationConfiguration : IEntityTypeConfiguration<GuardianVerification>
{
    public void Configure(EntityTypeBuilder<GuardianVerification> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.AttemptedAt });
        // All-student date-range verification report.
        b.HasIndex(x => x.AttemptedAt);
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasOne(x => x.Student)
            .WithMany(x => x.GuardianVerifications)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Guardian)
            .WithMany()
            .HasForeignKey(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ApprovedByAdmin)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByAdminId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
