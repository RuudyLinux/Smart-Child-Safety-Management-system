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
