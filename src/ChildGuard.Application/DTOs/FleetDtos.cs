using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.DTOs;

/// <summary>One student currently on a bus. Built from active assignments on every read, never stored.</summary>
public record RosterEntryDto(
    int AssignmentId, int StudentId, string StudentName, string? Grade, string? Section, string? AdmissionNumber,
    int BusId, string BusNumber, int? RouteId, string? RouteName, DateTime AssignedAt,
    int? PickupStopId = null, string? PickupStop = null, int? DropStopId = null, string? DropStop = null);

public record BusSummaryDto(int BusId, string BusNumber, string? RegistrationNumber, int? RouteId, string? RouteName, BusStatus Status, int StudentCount);

public record DriverSummaryDto(
    int DriverId, string FullName, string? Phone, string? Email, string? LicenseNumber, string? LicenseCategory,
    DateOnly? LicenseExpiresOn, LicenceState LicenceState, RecordStatus Status);

public record RouteSummaryDto(int RouteId, string RouteName, string? Description, RecordStatus Status,
    string? StartPoint = null, string? Destination = null, IReadOnlyList<RouteStopDto>? Stops = null);

public record DriverDetailDto(
    DriverDto Driver, AddressDto? Address, string? EmergencyContactName, string? EmergencyContactPhone, DateOnly? JoinedOn,
    DateTime CreatedAt, IReadOnlyList<BusSummaryDto> Buses, IReadOnlyList<RosterEntryDto> Students, DeletionCheckDto Deletion);

public record BusDetailDto(
    BusDto Bus, DateTime CreatedAt, DriverSummaryDto? Driver, RouteSummaryDto? Route,
    IReadOnlyList<RosterEntryDto> Students, DeletionCheckDto Deletion);

/// <summary>Full replacement of a driver's profile.</summary>
public record UpdateDriverProfileRequest(
    string FullName, string? Phone, string? LicenseNumber, string? EmployeeCode, string? Email, AddressDto? Address,
    string? EmergencyContactName, string? EmergencyContactPhone, string? LicenseCategory, DateOnly? LicenseExpiresOn, DateOnly? JoinedOn);

/// <summary>Vehicle details, kept separate from the bus's number, capacity, driver and route edit.</summary>
public record UpdateBusDetailsRequest(string? RegistrationNumber, string? Manufacturer, string? Model);

public record BusOperationalStatusRequest(BusStatus Status, string? Reason);

/// <param name="StudentsUsing">Students whose current assignment boards or leaves at this stop.</param>
public record RouteStopDto(int RouteStopId, int RouteId, int Sequence, string Name, string? Address,
    TimeOnly? PickupTime, TimeOnly? DropTime, RecordStatus Status, int StudentsUsing);

public record RouteDetailDto(RouteDto Route, DateTime CreatedAt, IReadOnlyList<RouteStopDto> Stops,
    IReadOnlyList<BusSummaryDto> Buses, IReadOnlyList<RosterEntryDto> Students, DeletionCheckDto Deletion);

public record RouteStopRequest(string Name, string? Address, TimeOnly? PickupTime, TimeOnly? DropTime);

/// <summary>Every active stop of the route, in the new order.</summary>
public record ReorderStopsRequest(IReadOnlyList<int> StopIds);

public record UpdateRouteDetailsRequest(string? StartPoint, string? Destination);

public record UpdateAssignmentStopsRequest(int? PickupStopId, int? DropStopId);

/// <summary>A student's current school transport, everything the profile shows in one place.</summary>
public record StudentTransportDto(
    int AssignmentId, int BusId, string BusNumber, string? RegistrationNumber, BusStatus BusStatus,
    int? RouteId, string? RouteName, string? PickupStop, TimeOnly? PickupTime, string? DropStop, TimeOnly? DropTime,
    int? DriverId, string? DriverName, string? DriverPhone, DateTime AssignedAt);

public record StudentTransportHistoryDto(int AssignmentId, string BusNumber, string? RouteName, string? PickupStop, string? DropStop,
    DateTime AssignedAt, DateTime? EndedAt, RecordStatus Status);
