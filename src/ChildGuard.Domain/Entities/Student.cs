using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Student
{
    public int StudentId { get; set; }
    public string FullName { get; set; } = default!;
    public DateTime DateOfBirth { get; set; }
    public string? Grade { get; set; }
    public string? Section { get; set; }
    /// <summary>School admission number. Optional; unique when present.</summary>
    public string? AdmissionNumber { get; set; }
    public Gender? Gender { get; set; }
    public PostalAddress? Address { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    /// <summary>Set when an admin archived the record (Status is then Inactive). Cleared on restore.</summary>
    public DateTime? ArchivedAt { get; set; }
    public int? ArchivedByAdminId { get; set; }
    public string? ArchiveReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public RfidCard? RfidCard { get; set; }
    /// <summary>Profile image, when one has been uploaded. Never loaded unless explicitly included.</summary>
    public StudentPhoto? Photo { get; set; }
    public ICollection<StudentGuardian> StudentGuardians { get; set; } = new List<StudentGuardian>();
    public ICollection<StudentBusAssignment> BusAssignments { get; set; } = new List<StudentBusAssignment>();
    public ICollection<Attendance> AttendanceRecords { get; set; } = new List<Attendance>();
    public ICollection<JourneyEvent> JourneyEvents { get; set; } = new List<JourneyEvent>();
    public ICollection<GuardianVerification> GuardianVerifications { get; set; } = new List<GuardianVerification>();
}
