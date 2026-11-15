using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ChildGuard.Infrastructure.Persistence;

public class ChildGuardDbContext : DbContext
{
    public ChildGuardDbContext(DbContextOptions<ChildGuardDbContext> options) : base(options) { }

    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<GuardianOtpChallenge> GuardianOtpChallenges => Set<GuardianOtpChallenge>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<RfidCard> RfidCards => Set<RfidCard>();
    public DbSet<RfidDevice> RfidDevices => Set<RfidDevice>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<StudentBusAssignment> StudentBusAssignments => Set<StudentBusAssignment>();
    public DbSet<Attendance> AttendanceRecords => Set<Attendance>();
    public DbSet<JourneyEvent> JourneyEvents => Set<JourneyEvent>();
    public DbSet<GuardianFaceData> GuardianFaceData => Set<GuardianFaceData>();
    public DbSet<GuardianFaceReference> GuardianFaceReferences => Set<GuardianFaceReference>();
    public DbSet<VerificationSession> VerificationSessions => Set<VerificationSession>();
    public DbSet<GuardianVerification> GuardianVerifications => Set<GuardianVerification>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<GuardianPhoto> GuardianPhotos => Set<GuardianPhoto>();
    public DbSet<StudentPhoto> StudentPhotos => Set<StudentPhoto>();
    public DbSet<FaceAuditLog> FaceAuditLogs => Set<FaceAuditLog>();
    public DbSet<GuardianTrustedDevice> GuardianTrustedDevices => Set<GuardianTrustedDevice>();
    public DbSet<TemporaryPickupAuthorization> TemporaryPickupAuthorizations => Set<TemporaryPickupAuthorization>();
    public DbSet<TemporaryPickupPhoto> TemporaryPickupPhotos => Set<TemporaryPickupPhoto>();
    public DbSet<TemporaryPickupAuditEntry> TemporaryPickupAuditEntries => Set<TemporaryPickupAuditEntry>();
    public DbSet<DataImport> DataImports => Set<DataImport>();
    public DbSet<TransportAuditEntry> TransportAuditEntries => Set<TransportAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChildGuardDbContext).Assembly);
        MarkInstantsAsUtc(modelBuilder);
        // The MySQL model (and its migration snapshot) is the reference. Everything PostgreSQL does
        // differently is applied here, only for Npgsql, so the MySQL snapshot never changes.
        if (Database.IsNpgsql()) ApplyPostgresMappings(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// PostgreSQL differences, kept next to each other so the contract is visible:
    ///  * LONGTEXT/LONGBLOB column types become text/bytea.
    ///  * Student.DateOfBirth stays a plain (zone-less) timestamp: it is a calendar value, and a
    ///    timestamptz column would reject its Unspecified kind.
    ///  * The computed one-active-bus-assignment key uses PostgreSQL identifier quoting.
    ///  * MySQL compares and sorts text case-insensitively (utf8mb4_unicode_ci); PostgreSQL does not.
    ///    Columns that are looked up by equality, are unique, or order a list use citext so logins,
    ///    uniqueness and list order behave the same on both engines.
    /// </summary>
    private static void ApplyPostgresMappings(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            switch ((property[RelationalAnnotationNames.ColumnType] as string)?.ToUpperInvariant())
            {
                case "LONGTEXT": property.SetColumnType("text"); break;
                case "LONGBLOB": property.SetColumnType("bytea"); break;
            }
        }

        modelBuilder.Entity<Student>().Property(x => x.DateOfBirth).HasColumnType("timestamp without time zone");

        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.Entity<Admin>().Property(x => x.Email).HasColumnType("citext");
        modelBuilder.Entity<Guardian>().Property(x => x.Email).HasColumnType("citext");
        modelBuilder.Entity<Guardian>().Property(x => x.Phone).HasColumnType("citext");
        modelBuilder.Entity<Guardian>().Property(x => x.FullName).HasColumnType("citext");
        modelBuilder.Entity<Student>().Property(x => x.FullName).HasColumnType("citext");
        modelBuilder.Entity<Driver>().Property(x => x.FullName).HasColumnType("citext");
        modelBuilder.Entity<Route>().Property(x => x.RouteName).HasColumnType("citext");
        modelBuilder.Entity<Bus>().Property(x => x.BusNumber).HasColumnType("citext");
        modelBuilder.Entity<RfidCard>().Property(x => x.CardUid).HasColumnType("citext");
        modelBuilder.Entity<RfidDevice>().Property(x => x.DeviceLabel).HasColumnType("citext");

        // Same one-active-assignment key as MySQL, in PostgreSQL's identifier quoting.
        modelBuilder.Entity<StudentBusAssignment>().Property(x => x.ActiveStudentKey)
            .HasComputedColumnSql("CASE WHEN \"Status\" = 1 THEN \"StudentId\" ELSE NULL END", stored: true);
    }

    /// <summary>
    /// Instants are stored in UTC (a write with Kind=Unspecified is taken to already be UTC, which
    /// Npgsql requires for timestamptz), but MySQL hands DateTime values back with
    /// Kind=Unspecified. Serialized like that they carry no "Z", and every client
    /// (browser, Flutter) reads them as local time. Re-stamp them as UTC on read.
    /// Calendar dates are not instants and are left alone.
    /// </summary>
    private static void MarkInstantsAsUtc(ModelBuilder modelBuilder)
    {
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue
                ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.Name == nameof(Student.DateOfBirth)) continue; // a calendar date, not an instant
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
            }
        }
    }
}
