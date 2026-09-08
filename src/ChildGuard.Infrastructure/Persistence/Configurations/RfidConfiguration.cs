using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class RfidCardConfiguration : IEntityTypeConfiguration<RfidCard>
{
    public void Configure(EntityTypeBuilder<RfidCard> b)
    {
        b.HasKey(x => x.RfidCardId);
        b.Property(x => x.CardUid).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.CardUid).IsUnique();
    }
}

public class RfidDeviceConfiguration : IEntityTypeConfiguration<RfidDevice>
{
    public void Configure(EntityTypeBuilder<RfidDevice> b)
    {
        b.HasKey(x => x.RfidDeviceId);
        b.Property(x => x.DeviceLabel).IsRequired().HasMaxLength(50);
        b.HasIndex(x => x.DeviceLabel).IsUnique();
    }
}
