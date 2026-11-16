using ChildGuard.Domain.Enums;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;

namespace ChildGuard.Application.Interfaces;

/// <summary>
/// Drivers, routes, buses and student bus assignments. Nothing is ever deleted: history keeps
/// referring to the same rows, which become Inactive instead. Every change is written to the
/// transport audit trail with the admin who made it (<c>actorAdminId</c>).
/// </summary>
public interface ITransportationService
{
    TransportSettingsDto GetSettings();

    Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default);
    Task<DriverDto> AddDriverAsync(CreateDriverRequest request, CancellationToken ct = default, int? actorAdminId = null);

    Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default);
    Task<Result<RouteDto>> AddRouteAsync(CreateRouteRequest request, CancellationToken ct = default, int? actorAdminId = null);

    Task<List<BusDto>> GetBusesAsync(CancellationToken ct = default);
    Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default, int? actorAdminId = null);

    Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default);

    /// <summary>Assigns (or moves) a student to a bus: ends any current assignment and creates the new one atomically.</summary>
    Task<Result> AddAssignmentAsync(CreateStudentBusAssignmentRequest request, CancellationToken ct = default, int? actorAdminId = null);

    Task<Result> UpdateDriverAsync(int id, UpdateDriverRequest request, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> SetDriverStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> UpdateRouteAsync(int id, UpdateRouteRequest request, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> SetRouteStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> UpdateBusAsync(int id, UpdateBusRequest request, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> SetBusStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null);

    /// <summary>Ends an assignment (Inactive). An ended assignment is history and is never reactivated.</summary>
    Task<Result> SetAssignmentStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null);

    Task<List<TransportAuditDto>> GetAuditAsync(int take, CancellationToken ct = default);

    // ---- profiles, live rosters and controlled deletion

    Task<Result<DriverDetailDto>> GetDriverDetailAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateDriverProfileAsync(int id, UpdateDriverProfileRequest request, CancellationToken ct = default, int? actorAdminId = null);
    /// <summary>Only for a driver who drives no bus; refused with the reason otherwise.</summary>
    Task<LifecycleOutcome> DeleteDriverAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null);

    Task<Result<BusDetailDto>> GetBusDetailAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateBusDetailsAsync(int id, UpdateBusDetailsRequest request, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> SetBusOperationalStatusAsync(int id, BusStatus status, string? reason, CancellationToken ct = default, int? actorAdminId = null);
    // ---- routes and stops

    Task<Result<RouteDetailDto>> GetRouteDetailAsync(int id, CancellationToken ct = default);
    Task<Result> UpdateRouteDetailsAsync(int id, UpdateRouteDetailsRequest request, CancellationToken ct = default, int? actorAdminId = null);
    /// <summary>Adds a stop at the end of the route.</summary>
    Task<Result<RouteStopDto>> AddStopAsync(int routeId, RouteStopRequest request, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> UpdateStopAsync(int routeId, int stopId, RouteStopRequest request, CancellationToken ct = default, int? actorAdminId = null);
    /// <summary>Deletes an unused stop; a stop any assignment used is deactivated instead (refused while a current assignment uses it).</summary>
    Task<Result<string>> RemoveStopAsync(int routeId, int stopId, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> SetStopStatusAsync(int routeId, int stopId, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null);
    Task<Result> ReorderStopsAsync(int routeId, ReorderStopsRequest request, CancellationToken ct = default, int? actorAdminId = null);
    /// <summary>Only for a route no bus uses and no assignment or journey ever used; refused otherwise.</summary>
    Task<LifecycleOutcome> DeleteRouteAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null);
    /// <summary>Sets the pickup and drop-off stops of an active assignment; both must be active stops on its route.</summary>
    Task<Result> UpdateAssignmentStopsAsync(int assignmentId, UpdateAssignmentStopsRequest request, CancellationToken ct = default, int? actorAdminId = null);

    /// <summary>Only for a bus that never carried a student or recorded a journey; refused otherwise.</summary>
    Task<LifecycleOutcome> DeleteBusAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null);
}
