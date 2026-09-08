using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class Route
{
    public int RouteId { get; set; }
    public string RouteName { get; set; } = default!;
    public string? Description { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Bus> Buses { get; set; } = new List<Bus>();
    public ICollection<StudentBusAssignment> StudentBusAssignments { get; set; } = new List<StudentBusAssignment>();
}
