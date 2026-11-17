using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class RfidCard
{
    public int RfidCardId { get; set; }
    public string CardUid { get; set; } = default!;
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
    public RfidCardStatus Status { get; set; } = RfidCardStatus.Active;
    public DateTime? AssignedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RfidCardAssignment> Assignments { get; set; } = new List<RfidCardAssignment>();
    public ICollection<Attendance> AttendanceRecords { get; set; } = new List<Attendance>();
    public ICollection<JourneyEvent> JourneyEvents { get; set; } = new List<JourneyEvent>();
}
