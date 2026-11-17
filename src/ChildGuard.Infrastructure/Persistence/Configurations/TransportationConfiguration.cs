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
        b.Property(x => x.EmployeeCode).HasMaxLength(30);
        b.HasIndex(x => x.EmployeeCode).IsUnique();
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.EmergencyContactName).HasMaxLength(150);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(30);
        b.Property(x => x.LicenseCategory).HasMaxLength(30);
        b.OwnsOne(x => x.Address, PostalAddressMapping.Map);
    }
}

public class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> b)
    {
        b.HasKey(x => x.RouteId);
        b.Property(x => x.RouteName).IsRequired().HasMaxLength(150);
        // Route names are unique (case-insensitive: MySQL collation, citext on PostgreSQL).
        b.HasIndex(x => x.RouteName).IsUnique();
        b.Property(x => x.StartPoint).HasMaxLength(150);
        b.Property(x => x.Destination).HasMaxLength(150);
    }
}

public class RouteStopConfiguration : IEntityTypeConfiguration<RouteStop>
{
    public void Configure(EntityTypeBuilder<RouteStop> b)
    {
        b.HasKey(x => x.RouteStopId);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(200);
        // Not unique: reordering rewrites sequences one row at a time. The service keeps them 1..n.
        b.HasIndex(x => new { x.RouteId, x.Sequence });
        b.HasOne(x => x.Route)
            .WithMany(x => x.Stops)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BusConfiguration : IEntityTypeConfiguration<Bus>
{
    public void Configure(EntityTypeBuilder<Bus> b)
    {
        b.HasKey(x => x.BusId);
        b.Property(x => x.BusNumber).IsRequired().HasMaxLength(30);
        b.HasIndex(x => x.BusNumber).IsUnique();
        b.Property(x => x.RegistrationNumber).HasMaxLength(20);
        b.HasIndex(x => x.RegistrationNumber).IsUnique();
        b.Property(x => x.Manufacturer).HasMaxLength(60);
        b.Property(x => x.Model).HasMaxLength(60);

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
        // Seats taken on a bus are counted from its active assignments.
        b.HasIndex(x => new { x.BusId, x.Status });

        // One active assignment per student, enforced by the database: the computed key is the
        // StudentId while Active and NULL otherwise, and unique indexes allow many NULLs.
        // (PostgreSQL gets the same expression with quoted names in ApplyPostgresMappings.)
        b.Property(x => x.ActiveStudentKey)
            .HasComputedColumnSql("CASE WHEN `Status` = 1 THEN `StudentId` ELSE NULL END", stored: true);
        b.HasIndex(x => x.ActiveStudentKey).IsUnique();

        b.HasOne(x => x.Student)
            .WithMany(x => x.BusAssignments)
            .HasForeignKey(x => x.StudentId)
            // Historical record: RESTRICT, like every other history table. Also required by MySQL 8, which refuses
            // CASCADE on a column that the stored ActiveStudentKey below is computed from.
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Bus)
            .WithMany(x => x.StudentBusAssignments)
            .HasForeignKey(x => x.BusId)
            .OnDelete(DeleteBehavior.Restrict); // historical record: never cascade-deleted (soft delete via Status)

        b.HasOne(x => x.Route)
            .WithMany(x => x.StudentBusAssignments)
            .HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.SetNull);
        // History: a stop used by any assignment is never deleted (it is deactivated instead).
        b.HasOne(x => x.PickupStop).WithMany().HasForeignKey(x => x.PickupStopId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DropStop).WithMany().HasForeignKey(x => x.DropStopId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TransportAuditEntryConfiguration : IEntityTypeConfiguration<TransportAuditEntry>
{
    public void Configure(EntityTypeBuilder<TransportAuditEntry> b)
    {
        b.ToTable("TransportAuditEntries");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(20);
        b.Property(x => x.Action).IsRequired().HasMaxLength(30);
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
