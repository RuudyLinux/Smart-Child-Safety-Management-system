using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class SchoolTimingSettingsConfiguration : IEntityTypeConfiguration<SchoolTimingSettings>
{
    public void Configure(EntityTypeBuilder<SchoolTimingSettings> b)
    {
        b.HasKey(x => x.SchoolTimingSettingsId);
        // A single row with a fixed id, written by the service, never generated.
        b.Property(x => x.SchoolTimingSettingsId).ValueGeneratedNever();
    }
}

public class SchoolCalendarDayConfiguration : IEntityTypeConfiguration<SchoolCalendarDay>
{
    public void Configure(EntityTypeBuilder<SchoolCalendarDay> b)
    {
        b.HasKey(x => x.SchoolCalendarDayId);
        b.Property(x => x.Title).IsRequired().HasMaxLength(100);
        // One row per date; a removed entry is reactivated rather than duplicated.
        b.HasIndex(x => x.Date).IsUnique();
    }
}
