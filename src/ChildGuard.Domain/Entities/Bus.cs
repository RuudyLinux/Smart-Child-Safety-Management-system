using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Bus
{
    public int BusId { get; set; }
    public string BusNumber { get; set; } = default!;
    /// <summary>Vehicle registration (number plate). Optional; unique when present.</summary>
    public string? RegistrationNumber { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public int Capacity { get; set; }
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }
    public int? RouteId { get; set; }
    public Route? Route { get; set; }
    public BusStatus Status { get; set; } = BusStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudentBusAssignment> StudentBusAssignments { get; set; } = new List<StudentBusAssignment>();
}
