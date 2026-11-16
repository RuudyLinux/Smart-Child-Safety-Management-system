using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <param name="BusNumbers">Buses this driver currently drives.</param>
/// <param name="StudentCount">Students currently assigned to those buses (live count).</param>
public record DriverDto(int DriverId, string FullName, string? Phone, string? LicenseNumber, RecordStatus Status,
    string? EmployeeCode = null, string? Email = null, string? LicenseCategory = null, DateOnly? LicenseExpiresOn = null,
    LicenceState LicenceState = LicenceState.NotRecorded, IReadOnlyList<string>? BusNumbers = null, int StudentCount = 0);
public record CreateDriverRequest(string FullName, string? Phone, string? LicenseNumber);

public record RouteDto(int RouteId, string RouteName, string? Description, RecordStatus Status,
    string? StartPoint = null, string? Destination = null, int StopCount = 0);
public record CreateRouteRequest(string RouteName, string? Description);

/// <param name="ActiveStudentCount">Students currently assigned (seats taken). Recalculated on every read.</param>
public record BusDto(int BusId, string BusNumber, int Capacity, string? DriverName, string? RouteName, BusStatus Status,
    int? DriverId = null, int? RouteId = null, int ActiveStudentCount = 0,
    string? RegistrationNumber = null, string? Manufacturer = null, string? Model = null);
public record CreateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

public record StudentBusAssignmentDto(int Id, int StudentId, string StudentName, string BusNumber, string? RouteName, RecordStatus Status, DateTime AssignedAt,
    int BusId = 0, int? RouteId = null, DateTime? EndedAt = null,
    int? PickupStopId = null, string? PickupStop = null, int? DropStopId = null, string? DropStop = null);

/// <summary>
/// Assigns a student to a bus. Any current assignment is ended in the same transaction.
/// <paramref name="RouteId"/> may be omitted: the bus's own route is used. A different route is
/// accepted only when the school allows route overrides (Transport:AllowRouteOverride).
/// </summary>
/// <param name="PickupStopId">Optional; must be an active stop on the assignment's route.</param>
/// <param name="DropStopId">Optional; must be an active stop on the assignment's route.</param>
public record CreateStudentBusAssignmentRequest(int StudentId, int BusId, int? RouteId, int? PickupStopId = null, int? DropStopId = null);

public record TransportSettingsDto(bool AllowRouteOverride);

public record TransportAuditDto(long Id, DateTime OccurredAt, int? AdminId, string? AdminName,
    string EntityType, int EntityId, string Action, string? Detail);

public record UpdateDriverRequest(string FullName, string? Phone, string? LicenseNumber);
public record UpdateRouteRequest(string RouteName, string? Description);
public record UpdateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

/// <summary>Activate or deactivate a driver, route, bus or bus assignment. Records are never deleted.</summary>
public record UpdateTransportStatusRequest(RecordStatus Status);
