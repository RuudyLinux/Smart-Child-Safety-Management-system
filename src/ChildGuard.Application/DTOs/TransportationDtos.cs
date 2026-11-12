using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record DriverDto(int DriverId, string FullName, string? Phone, string? LicenseNumber, RecordStatus Status);
public record CreateDriverRequest(string FullName, string? Phone, string? LicenseNumber);

public record RouteDto(int RouteId, string RouteName, string? Description, RecordStatus Status);
public record CreateRouteRequest(string RouteName, string? Description);

public record BusDto(int BusId, string BusNumber, int Capacity, string? DriverName, string? RouteName, RecordStatus Status,
    int? DriverId = null, int? RouteId = null);
public record CreateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

public record StudentBusAssignmentDto(int Id, int StudentId, string StudentName, string BusNumber, string? RouteName, RecordStatus Status, DateTime AssignedAt);
public record CreateStudentBusAssignmentRequest(int StudentId, int BusId, int? RouteId);

public record UpdateDriverRequest(string FullName, string? Phone, string? LicenseNumber);
public record UpdateRouteRequest(string RouteName, string? Description);
public record UpdateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

/// <summary>Activate or deactivate a driver, route, bus or bus assignment. Records are never deleted.</summary>
public record UpdateTransportStatusRequest(RecordStatus Status);
