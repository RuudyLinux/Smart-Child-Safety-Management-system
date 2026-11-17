using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.HasKey(x => x.StudentId);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        b.Property(x => x.Grade).HasMaxLength(50);
        b.Property(x => x.Section).HasMaxLength(20);
        b.Property(x => x.AdmissionNumber).HasMaxLength(50);
        // Unique when present; NULLs (no number recorded) are allowed many times on both engines.
        b.HasIndex(x => x.AdmissionNumber).IsUnique();
        b.OwnsOne(x => x.Address, PostalAddressMapping.Map);
        b.Property(x => x.ArchiveReason).HasMaxLength(500);

        b.HasOne(x => x.RfidCard)
            .WithOne(x => x.Student)
            .HasForeignKey<RfidCard>(x => x.StudentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Relationship).HasMaxLength(50);
        b.HasIndex(x => new { x.StudentId, x.GuardianId }).IsUnique();

        b.HasOne(x => x.Student)
            .WithMany(x => x.StudentGuardians)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Guardian)
            .WithMany(x => x.StudentGuardians)
            .HasForeignKey(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
