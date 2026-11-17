using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Driver
{
    public int DriverId { get; set; }
    public string FullName { get; set; } = default!;
    public string? Phone { get; set; }
    public string? LicenseNumber { get; set; }
    /// <summary>The school's staff / driver ID. Optional; unique when present.</summary>
    public string? EmployeeCode { get; set; }
    public string? Email { get; set; }
    public PostalAddress? Address { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    /// <summary>Licence class, e.g. "HTV" or "LMV-TR".</summary>
    public string? LicenseCategory { get; set; }
    public DateOnly? LicenseExpiresOn { get; set; }
    public DateOnly? JoinedOn { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Bus> Buses { get; set; } = new List<Bus>();
    public DriverPhoto? Photo { get; set; }
}
