using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class VerificationSessionConfiguration : IEntityTypeConfiguration<VerificationSession>
{
    public void Configure(EntityTypeBuilder<VerificationSession> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ChallengeTokenHash).HasMaxLength(64);
        b.Property(x => x.ChallengeSteps).HasMaxLength(40);
        b.Property(x => x.NeutralFrameHash).HasMaxLength(64);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.StudentId, x.State });
        // A captured burst can be submitted once: NULLs (sessions with no live capture yet) do not collide.
        b.HasIndex(x => x.NeutralFrameHash).IsUnique();

        b.HasOne(x => x.Student)
            .WithMany()
            .HasForeignKey(x => x.StudentId)
            .OnDelete(DeleteBehavior.Restrict); // historical record: never cascade-deleted (soft delete via Status)
    }
}
