using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class StudentBusAssignment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int BusId { get; set; }
    public Bus Bus { get; set; } = default!;
    public int? RouteId { get; set; }
    public Route? Route { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
