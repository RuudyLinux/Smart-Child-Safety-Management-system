using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> b)
    {
        b.HasKey(x => x.DriverId);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.LicenseNumber).HasMaxLength(50);
    }
}

public class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> b)
    {
        b.HasKey(x => x.RouteId);
        b.Property(x => x.RouteName).IsRequired().HasMaxLength(150);
    }
}

public class BusConfiguration : IEntityTypeConfiguration<Bus>
{
    public void Configure(EntityTypeBuilder<Bus> b)
    {
        b.HasKey(x => x.BusId);
        b.Property(x => x.BusNumber).IsRequired().HasMaxLength(30);
        b.HasIndex(x => x.BusNumber).IsUnique();

        b.HasOne(x => x.Driver)
            .WithMany(x => x.Buses)
            .HasForeignKey(x => x.DriverId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Route)
            .WithMany(x => x.Buses)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class StudentBusAssignmentConfiguration : IEntityTypeConfiguration<StudentBusAssignment>
{
    public void Configure(EntityTypeBuilder<StudentBusAssignment> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StudentId, x.BusId });

        b.HasOne(x => x.Student)
            .WithMany(x => x.BusAssignments)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Bus)
            .WithMany(x => x.StudentBusAssignments)
            .HasForeignKey(x => x.BusId)
            .OnDelete(DeleteBehavior.Restrict); // historical record: never cascade-deleted (soft delete via Status)

        b.HasOne(x => x.Route)
            .WithMany(x => x.StudentBusAssignments)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
