using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Attendance
{
    public int AttendanceId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int? RfidCardId { get; set; }
    public RfidCard? RfidCard { get; set; }
    public AttendanceEventType EventType { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
