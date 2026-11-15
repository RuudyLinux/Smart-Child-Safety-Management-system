using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Services;

/// <summary>
/// Stamps a new journey event with the student's bus assignment as it is at that moment: the
/// assignment, its bus and the route it carries. Called inside the per-student serialized
/// section, so the snapshot matches the state the event was validated against. Later changes
/// to the bus (driver, route, service status) or ending the assignment never touch the event.
/// </summary>
public static class JourneyTransportSnapshot
{
    public static async Task StampAsync(IJourneyRepository journey, JourneyEvent journeyEvent, CancellationToken ct)
    {
        var assignment = await journey.GetActiveBusAssignmentAsync(journeyEvent.StudentId, ct);
        if (assignment is null) return;
        journeyEvent.StudentBusAssignmentId = assignment.Id;
        journeyEvent.BusId = assignment.BusId;
        journeyEvent.RouteId = assignment.RouteId;
    }
}
