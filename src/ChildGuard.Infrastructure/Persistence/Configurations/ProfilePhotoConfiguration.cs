using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianPhotoConfiguration : IEntityTypeConfiguration<GuardianPhoto>
{
    public void Configure(EntityTypeBuilder<GuardianPhoto> b)
    {
        b.ToTable("GuardianPhotos");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(50);
        b.Property(x => x.Data).IsRequired().HasColumnType("LONGBLOB");
        // One image per owner: a replace overwrites rather than accumulating rows.
        b.HasIndex(x => x.GuardianId).IsUnique();

        b.HasOne(x => x.Guardian)
            .WithOne(x => x.Photo)
            .HasForeignKey<GuardianPhoto>(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StudentPhotoConfiguration : IEntityTypeConfiguration<StudentPhoto>
{
    public void Configure(EntityTypeBuilder<StudentPhoto> b)
    {
        b.ToTable("StudentPhotos");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(50);
        b.Property(x => x.Data).IsRequired().HasColumnType("LONGBLOB");
        b.HasIndex(x => x.StudentId).IsUnique();

        b.HasOne(x => x.Student)
            .WithOne(x => x.Photo)
            .HasForeignKey<StudentPhoto>(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DriverPhotoConfiguration : IEntityTypeConfiguration<DriverPhoto>
{
    public void Configure(EntityTypeBuilder<DriverPhoto> b)
    {
        b.ToTable("DriverPhotos");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(50);
        b.Property(x => x.Data).IsRequired().HasColumnType("LONGBLOB");
        b.HasIndex(x => x.DriverId).IsUnique();

        b.HasOne(x => x.Driver)
            .WithOne(x => x.Photo)
            .HasForeignKey<DriverPhoto>(x => x.DriverId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
