using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

public record DriverDto(int DriverId, string FullName, string? Phone, string? LicenseNumber, RecordStatus Status);
public record CreateDriverRequest(string FullName, string? Phone, string? LicenseNumber);

public record RouteDto(int RouteId, string RouteName, string? Description, RecordStatus Status);
public record CreateRouteRequest(string RouteName, string? Description);

public record BusDto(int BusId, string BusNumber, int Capacity, string? DriverName, string? RouteName, RecordStatus Status);
public record CreateBusRequest(string BusNumber, int Capacity, int? DriverId, int? RouteId);

public record StudentBusAssignmentDto(int Id, int StudentId, string StudentName, string BusNumber, string? RouteName, RecordStatus Status, DateTime AssignedAt);
public record CreateStudentBusAssignmentRequest(int StudentId, int BusId, int? RouteId);
