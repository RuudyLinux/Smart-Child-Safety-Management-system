using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class JourneyEvent
{
    public int JourneyEventId { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int? RfidCardId { get; set; }
    public RfidCard? RfidCard { get; set; }
    public int? RfidDeviceId { get; set; }
    public RfidDevice? RfidDevice { get; set; }
    public JourneyEventType EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public EventSource Source { get; set; } = EventSource.Rfid;

    /// <summary>
    /// Optional idempotency key sent by the scanner for this tap (unique per device).
    /// A retry carrying the same key returns the original result instead of re-scanning.
    /// </summary>
    public string? ScanId { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
