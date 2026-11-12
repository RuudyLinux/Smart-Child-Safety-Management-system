using ChildGuard.Domain.Entities;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChildGuardDbContext).Assembly);
        MarkInstantsAsUtc(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Instants are stored in UTC, but MySQL hands DateTime values back with
    /// Kind=Unspecified. Serialized like that they carry no "Z", and every client
    /// (browser, Flutter) reads them as local time. Re-stamp them as UTC on read.
    /// Calendar dates are not instants and are left alone.
    /// </summary>
    private static void MarkInstantsAsUtc(ModelBuilder modelBuilder)
    {
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue && v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v,
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
