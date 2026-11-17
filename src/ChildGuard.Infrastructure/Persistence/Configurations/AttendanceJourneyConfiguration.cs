using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> b)
    {
        b.HasKey(x => x.AttendanceId);
        b.HasIndex(x => new { x.StudentId, x.Date, x.EventType });
        b.Property(x => x.RecordedAtUtc).IsRequired();

        b.HasOne(x => x.Student)
            .WithMany(x => x.AttendanceRecords)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict); // historical record: never cascade-deleted (soft delete via Status)

        b.HasOne(x => x.RfidCard)
            .WithMany(x => x.AttendanceRecords)
            .HasForeignKey(x => x.RfidCardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class JourneyEventConfiguration : IEntityTypeConfiguration<JourneyEvent>
{
    public void Configure(EntityTypeBuilder<JourneyEvent> b)
    {
        b.HasKey(x => x.JourneyEventId);
        b.HasIndex(x => new { x.StudentId, x.Timestamp });
        // All-student date-range report (paged journey report) seeks on Timestamp alone.
        b.HasIndex(x => x.Timestamp);

        b.Property(x => x.ScanId).HasMaxLength(64);
        // Idempotency: one recorded event per scanner-supplied key per device.
        // MySQL unique indexes allow many NULLs, so scans without a key are unaffected.
        b.HasIndex(x => new { x.RfidDeviceId, x.ScanId }).IsUnique();

        b.HasOne(x => x.Student)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict); // historical record: never cascade-deleted (soft delete via Status)

        b.HasOne(x => x.RfidCard)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.RfidCardId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.RfidDevice)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.RfidDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        // Transport snapshot: RESTRICT so the history can never lose (or silently null) the bus,
        // route or assignment it was recorded against.
        b.HasOne(x => x.Bus).WithMany().HasForeignKey(x => x.BusId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Route).WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.StudentBusAssignment).WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.StudentBusAssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
