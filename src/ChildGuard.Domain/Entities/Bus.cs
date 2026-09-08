using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Bus
{
    public int BusId { get; set; }
    public string BusNumber { get; set; } = default!;
    public int Capacity { get; set; }
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }
    public int? RouteId { get; set; }
    public Route? Route { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StudentBusAssignment> StudentBusAssignments { get; set; } = new List<StudentBusAssignment>();
}
