using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class FaceAuditLogConfiguration : IEntityTypeConfiguration<FaceAuditLog>
{
    public void Configure(EntityTypeBuilder<FaceAuditLog> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ActorRole).IsRequired().HasMaxLength(20);
        b.Property(x => x.Action).IsRequired().HasMaxLength(40);
        b.Property(x => x.Outcome).IsRequired().HasMaxLength(40);
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => x.GuardianId);
        b.HasIndex(x => x.StudentId);
    }
}
