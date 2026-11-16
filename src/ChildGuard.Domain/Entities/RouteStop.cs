using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

/// <summary>
/// One stop on a route, in travel order. A stop that a student assignment ever used is kept (deactivated) rather than
/// deleted, so transport history keeps pointing at it.
/// </summary>
public class RouteStop
{
    public int RouteStopId { get; set; }
    public int RouteId { get; set; }
    public Route Route { get; set; } = default!;
    /// <summary>1-based position along the route.</summary>
    public int Sequence { get; set; }
    public string Name { get; set; } = default!;
    public string? Address { get; set; }
    /// <summary>Scheduled morning pickup time at this stop (school-local).</summary>
    public TimeOnly? PickupTime { get; set; }
    /// <summary>Scheduled afternoon drop-off time at this stop (school-local).</summary>
    public TimeOnly? DropTime { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
