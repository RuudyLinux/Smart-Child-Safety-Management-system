using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class TemporaryPickupAuthorizationConfiguration : IEntityTypeConfiguration<TemporaryPickupAuthorization>
{
    public void Configure(EntityTypeBuilder<TemporaryPickupAuthorization> b)
    {
        b.ToTable("TemporaryPickupAuthorizations");
        b.HasKey(x => x.Id);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(100);
        b.Property(x => x.Phone).IsRequired().HasMaxLength(20);
        b.Property(x => x.Relationship).IsRequired().HasMaxLength(50);
        b.Property(x => x.IdentityNote).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.Property(x => x.QrNonce).HasMaxLength(64);
        b.Property(x => x.FaceEmbeddingJson).IsRequired().HasColumnType("LONGTEXT");
        b.Property(x => x.FaceModel).HasMaxLength(50);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasIndex(x => new { x.StudentId, x.ValidUntilUtc });
        b.HasIndex(x => new { x.Status, x.ValidUntilUtc });
        b.HasIndex(x => x.RequestedByGuardianId);

        // History: RESTRICT, never cascade (students and guardians are deactivated, not deleted).
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RequestedByGuardian).WithMany().HasForeignKey(x => x.RequestedByGuardianId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DecidedByAdmin).WithMany().HasForeignKey(x => x.DecidedByAdminId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CompletedByAdmin).WithMany().HasForeignKey(x => x.CompletedByAdminId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TemporaryPickupPhotoConfiguration : IEntityTypeConfiguration<TemporaryPickupPhoto>
{
    public void Configure(EntityTypeBuilder<TemporaryPickupPhoto> b)
    {
        b.ToTable("TemporaryPickupPhotos");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(50);
        b.Property(x => x.Data).IsRequired().HasColumnType("LONGBLOB");
        b.HasIndex(x => x.TemporaryPickupAuthorizationId).IsUnique();

        b.HasOne(x => x.Authorization)
            .WithOne(x => x.Photo)
            .HasForeignKey<TemporaryPickupPhoto>(x => x.TemporaryPickupAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TemporaryPickupAuditEntryConfiguration : IEntityTypeConfiguration<TemporaryPickupAuditEntry>
{
    public void Configure(EntityTypeBuilder<TemporaryPickupAuditEntry> b)
    {
        b.ToTable("TemporaryPickupAuditEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).IsRequired().HasMaxLength(40);
        b.Property(x => x.ActorRole).IsRequired().HasMaxLength(20);
        b.Property(x => x.Detail).HasMaxLength(500);
        b.HasIndex(x => new { x.AuthorizationId, x.OccurredAt });
        b.HasIndex(x => x.StudentId);
    }
}
