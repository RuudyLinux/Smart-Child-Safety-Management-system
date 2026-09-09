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
    public DbSet<GuardianVerification> GuardianVerifications => Set<GuardianVerification>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChildGuardDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
