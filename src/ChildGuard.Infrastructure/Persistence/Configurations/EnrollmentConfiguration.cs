using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

/// <summary>Column layout of a <see cref="PostalAddress"/> on its owner's table (AddressLine1, AddressCity, ...).</summary>
internal static class PostalAddressMapping
{
    public static void Map<TOwner>(OwnedNavigationBuilder<TOwner, PostalAddress> a) where TOwner : class
    {
        a.Property(x => x.Line1).HasColumnName("AddressLine1").HasMaxLength(200);
        a.Property(x => x.Line2).HasColumnName("AddressLine2").HasMaxLength(200);
        a.Property(x => x.Locality).HasColumnName("AddressLocality").HasMaxLength(100);
        a.Property(x => x.Landmark).HasColumnName("AddressLandmark").HasMaxLength(100);
        a.Property(x => x.City).HasColumnName("AddressCity").HasMaxLength(100);
        a.Property(x => x.District).HasColumnName("AddressDistrict").HasMaxLength(100);
        a.Property(x => x.State).HasColumnName("AddressState").HasMaxLength(100);
        a.Property(x => x.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
        a.Property(x => x.Country).HasColumnName("AddressCountry").HasMaxLength(100);
    }
}

public class RfidCardAssignmentConfiguration : IEntityTypeConfiguration<RfidCardAssignment>
{
    public void Configure(EntityTypeBuilder<RfidCardAssignment> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.RfidCardId, x.RecordedAt });
        b.HasIndex(x => x.StudentId);
        // History: RESTRICT, like every other history table.
        b.HasOne(x => x.RfidCard)
            .WithMany(x => x.Assignments)
            .HasForeignKey(x => x.RfidCardId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class EnrollmentRequestConfiguration : IEntityTypeConfiguration<EnrollmentRequest>
{
    public void Configure(EntityTypeBuilder<EnrollmentRequest> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(64);
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Property(x => x.RequestHash).IsRequired().HasMaxLength(64).IsFixedLength();
        b.Property(x => x.DuplicateOverrideReason).HasMaxLength(500);
        b.Property(x => x.ConfirmedInactiveGuardianIds).HasMaxLength(200);
        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
