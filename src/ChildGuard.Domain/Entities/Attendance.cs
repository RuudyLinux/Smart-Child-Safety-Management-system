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
    /// <summary>The instant the arrival/departure was recorded (UTC). Source of truth.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>School-local date of <see cref="RecordedAtUtc"/> (School:TimeZone).</summary>
    public DateOnly Date { get; set; }

    /// <summary>School-local time of <see cref="RecordedAtUtc"/>; compared to the late cutoff.</summary>
    public TimeOnly Time { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
