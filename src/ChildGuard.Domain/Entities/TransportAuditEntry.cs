namespace ChildGuard.Domain.Entities;

/// <summary>
/// Append-only record of one administrative transport change (driver, route, bus or student
/// assignment). Plain id columns, no foreign keys: the trail must never block or be removed
/// with the records it describes. Detail holds a short before → after description.
/// </summary>
public class TransportAuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? AdminId { get; set; }
    /// <summary>Driver, Route, Bus or Assignment.</summary>
    public string EntityType { get; set; } = default!;
    public int EntityId { get; set; }
    /// <summary>Created, Updated, Activated, Deactivated, Assigned, Reassigned, Ended.</summary>
    public string Action { get; set; } = default!;
    public string? Detail { get; set; }
}
