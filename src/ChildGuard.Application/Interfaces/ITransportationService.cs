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
}
