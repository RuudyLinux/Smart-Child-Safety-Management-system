using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> b)
    {
        b.HasKey(x => x.GuardianId);
        b.Property(x => x.FullName).IsRequired().HasMaxLength(150);
        b.Property(x => x.Email).IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.PasswordHash).IsRequired();
        b.OwnsOne(x => x.Address, PostalAddressMapping.Map);
        b.HasIndex(x => x.Email).IsUnique();
        // The contact number is the Guardian login identifier, so it must resolve to
        // exactly one account. Nulls stay allowed (MySQL permits duplicate NULLs) —
        // a Guardian with no contact number simply cannot receive an OTP or sign in.
        b.HasIndex(x => x.Phone).IsUnique();

        b.HasOne(x => x.FaceData)
            .WithOne(x => x.Guardian)
            .HasForeignKey<GuardianFaceData>(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class GuardianOtpChallengeConfiguration : IEntityTypeConfiguration<GuardianOtpChallenge>
{
    public void Configure(EntityTypeBuilder<GuardianOtpChallenge> b)
    {
        b.HasKey(x => x.GuardianOtpChallengeId);
        b.Property(x => x.ChallengeToken).IsRequired();
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.ChallengeToken).IsUnique();
        // Lookup path for "the open challenge(s) of this Guardian".
        b.HasIndex(x => new { x.GuardianId, x.IsConsumed });

        b.HasOne(x => x.Guardian)
            .WithMany(x => x.OtpChallenges)
            .HasForeignKey(x => x.GuardianId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
