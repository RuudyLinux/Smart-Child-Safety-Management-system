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

        b.HasOne(x => x.Student)
            .WithMany(x => x.AttendanceRecords)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

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

        b.HasOne(x => x.Student)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.RfidCard)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.RfidCardId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.RfidDevice)
            .WithMany(x => x.JourneyEvents)
            .HasForeignKey(x => x.RfidDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
