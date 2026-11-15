using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record DriverDto(int DriverId, string FullName, string? Phone, string? LicenseNumber, RecordStatus Status);
public record CreateDriverRequest(string FullName, string? Phone, string? LicenseNumber);

public record RouteDto(int RouteId, string RouteName, string? Description, RecordStatus Status);
public record CreateRouteRequest(string RouteName, string? Description);

/// <param name="ActiveStudentCount">Students currently assigned (seats taken). Recalculated on every read.</param>
public record BusDto(int BusId, string BusNumber, int Capacity, string? DriverName, string? RouteName, RecordStatus Status,
    int? DriverId = null, int? RouteId = null, int ActiveStudentCount = 0);
public record CreateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

public record StudentBusAssignmentDto(int Id, int StudentId, string StudentName, string BusNumber, string? RouteName, RecordStatus Status, DateTime AssignedAt,
    int BusId = 0, int? RouteId = null, DateTime? EndedAt = null);

/// <summary>
/// Assigns a student to a bus. Any current assignment is ended in the same transaction.
/// <paramref name="RouteId"/> may be omitted: the bus's own route is used. A different route is
/// accepted only when the school allows route overrides (Transport:AllowRouteOverride).
/// </summary>
public record CreateStudentBusAssignmentRequest(int StudentId, int BusId, int? RouteId);

public record TransportSettingsDto(bool AllowRouteOverride);

public record TransportAuditDto(long Id, DateTime OccurredAt, int? AdminId, string? AdminName,
    string EntityType, int EntityId, string Action, string? Detail);

public record UpdateDriverRequest(string FullName, string? Phone, string? LicenseNumber);
public record UpdateRouteRequest(string RouteName, string? Description);
public record UpdateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

/// <summary>Activate or deactivate a driver, route, bus or bus assignment. Records are never deleted.</summary>
public record UpdateTransportStatusRequest(RecordStatus Status);
