using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChildGuard.Infrastructure.Persistence.Configurations;

public class DataImportConfiguration : IEntityTypeConfiguration<DataImport>
{
    public void Configure(EntityTypeBuilder<DataImport> b)
    {
        b.ToTable("DataImports");
        b.HasKey(x => x.DataImportId);
        b.Property(x => x.Category).HasConversion<int>();
        b.Property(x => x.Source).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.FileName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Message).HasMaxLength(500);
        b.Property(x => x.ErrorReportJson).HasColumnType("LONGTEXT");

        b.HasIndex(x => x.StartedAt);
        b.HasIndex(x => new { x.Category, x.StartedAt });

        // Audit history: the admin who ran an import can be deactivated, never deleted out from under it.
        b.HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
    }
}
