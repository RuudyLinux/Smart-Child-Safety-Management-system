using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// Tracks a physical RFID scanner's identity/connection status.
/// Needed so the demo/verification page can display "RFID-001 Connected"
/// from data instead of a hardcoded string, and to keep reader count configurable.
/// </summary>
public class RfidDevice
{
    public int RfidDeviceId { get; set; }
    public string DeviceLabel { get; set; } = default!;
    public RfidConnectionStatus ConnectionStatus { get; set; } = RfidConnectionStatus.Disconnected;
    public DateTime? LastSeenAt { get; set; }

    public ICollection<JourneyEvent> JourneyEvents { get; set; } = new List<JourneyEvent>();
}
