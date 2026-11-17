using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianFaceReferenceConfiguration : IEntityTypeConfiguration<GuardianFaceReference>
{
    public void Configure(EntityTypeBuilder<GuardianFaceReference> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.EmbeddingJson).IsRequired().HasColumnType("LONGTEXT");
        b.Property(x => x.Model).IsRequired().HasMaxLength(50);
        b.Property(x => x.Label).HasMaxLength(60);
        b.Property(x => x.QualityJson).HasMaxLength(1000);
        b.Property(x => x.CreatedByRole).IsRequired().HasMaxLength(20);
        b.HasIndex(x => new { x.GuardianId, x.IsActive });

        b.HasOne(x => x.Guardian)
            .WithMany(x => x.FaceReferences)
            .HasForeignKey(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Restrict); // biometric record: never cascade-deleted with the guardian
    }
}
