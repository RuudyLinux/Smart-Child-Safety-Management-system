using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Driver
{
    public int DriverId { get; set; }
    public string FullName { get; set; } = default!;
    public string? Phone { get; set; }
    public string? LicenseNumber { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Bus> Buses { get; set; } = new List<Bus>();
}
