using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class AdminConfiguration : IEntityTypeConfiguration<Admin>
{
    public void Configure(EntityTypeBuilder<Admin> b)
    {
        b.HasKey(x => x.AdminId);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        b.Property(x => x.Email).IsRequired().HasMaxLength(200);
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
    }
}
