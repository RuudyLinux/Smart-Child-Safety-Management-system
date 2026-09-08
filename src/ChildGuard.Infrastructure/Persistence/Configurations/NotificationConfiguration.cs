using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.HasKey(x => x.NotificationId);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Message).IsRequired().HasMaxLength(1000);
        b.HasIndex(x => new { x.GuardianId, x.IsRead });

        b.HasOne(x => x.Guardian)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
