using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class TransportationService : ITransportationService
{
    public const string DriverNotFound = "Driver not found.";
    public const string RouteNotFound = "Route not found.";
    public const string BusNotFound = "Bus not found.";
    public const string AssignmentNotFound = "Assignment not found.";
    public const string StudentNotFound = "Student not found.";
    public const string DriverInactive = "This driver is deactivated and cannot be assigned to a bus.";
    public const string RouteInactive = "This route is deactivated and cannot be newly assigned.";
    public const string BusOutOfService = "This bus is out of service. Students cannot be assigned to it.";
    public const string RouteOverrideNotAllowed = "Students travel on their bus's own route. A different route is not allowed by this school's transport settings.";
    public const string AlreadyOnThisBus = "The student is already assigned to this bus.";
    public const string AssignmentChangedConcurrently = "This student's assignment was changed at the same moment by someone else. Reload and try again.";
    public const string EndedAssignmentIsHistory = "An ended assignment is kept as history and cannot be reactivated. Assign the student to a bus instead.";
    public const string DuplicateBusNumber = "A bus with this number already exists.";
    public const string DuplicateRouteName = "A route with this name already exists.";

    private readonly ITransportationRepository _repo;
    private readonly SchoolClock _clock;
    private readonly TransportOptions _options;

    private readonly IAuditTrail _audit;

    public TransportationService(ITransportationRepository repo, SchoolClock clock, TransportOptions options, IAuditTrail? audit = null)
    {
        _audit = audit ?? NullAuditTrail.Instance;
        _repo = repo;
        _clock = clock;
        _options = options;
    }

    public TransportSettingsDto GetSettings() => new(_options.AllowRouteOverride);

    public static string BusFull(string busNumber, int capacity) =>
        $"Bus {busNumber} is full ({capacity} of {capacity} seats taken).";

    public static string CapacityBelowOccupancy(int capacity, int active) =>
        $"Capacity cannot be {capacity}: {active} student(s) are currently assigned to this bus. End or move assignments first.";

    // ------------------------------------------------------------------ drivers

    public async Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default)
    {
        var buses = await _repo.GetBusesAsync(ct);
        var counts = await _repo.GetActiveAssignmentCountsAsync(ct);
        return (await _repo.GetDriversAsync(ct)).Select(d => ToDto(d, buses, counts)).ToList();
    }

    private DriverDto ToDto(Driver d, IReadOnlyCollection<Bus> buses, IReadOnlyDictionary<int, int> counts)
    {
        var driven = buses.Where(b => b.DriverId == d.DriverId).ToList();
        return new DriverDto(d.DriverId, d.FullName, d.Phone, d.LicenseNumber, d.Status,
            d.EmployeeCode, d.Email, d.LicenseCategory, d.LicenseExpiresOn, LicenceRules.State(d.LicenseExpiresOn, _clock.Today),
            driven.Select(b => b.BusNumber).OrderBy(n => n).ToList(),
            driven.Sum(b => counts.TryGetValue(b.BusId, out var n) ? n : 0));
    }

    private static List<RosterEntryDto> Roster(IEnumerable<StudentBusAssignment> active) =>
        active.Select(a => new RosterEntryDto(a.Id, a.StudentId, a.Student.FullName, a.Student.Grade, a.Student.Section, a.Student.AdmissionNumber,
            a.BusId, a.Bus.BusNumber, a.RouteId, a.Route?.RouteName, a.AssignedAt,
            a.PickupStopId, a.PickupStop?.Name, a.DropStopId, a.DropStop?.Name)).ToList();

    public async Task<Result<DriverDetailDto>> GetDriverDetailAsync(int id, CancellationToken ct = default)
    {
        var d = await _repo.GetDriverByIdAsync(id, ct);
        if (d is null) return Result<DriverDetailDto>.Failure(DriverNotFound);
        var buses = (await _repo.GetBusesAsync(ct)).Where(b => b.DriverId == id).ToList();
        var counts = await _repo.GetActiveAssignmentCountsAsync(ct);
        var roster = Roster(await _repo.GetActiveRosterAsync(buses.Select(b => b.BusId).ToList(), ct));
        return Result<DriverDetailDto>.Success(new DriverDetailDto(
            ToDto(d, buses, counts), AddressDto.From(d.Address), d.EmergencyContactName, d.EmergencyContactPhone, d.JoinedOn, d.CreatedAt,
            buses.Select(b => new BusSummaryDto(b.BusId, b.BusNumber, b.RegistrationNumber, b.RouteId, b.Route?.RouteName, b.Status,
                counts.TryGetValue(b.BusId, out var n) ? n : 0)).ToList(),
            roster, DriverCheck(buses)));
    }

    private static DeletionCheckDto DriverCheck(IReadOnlyCollection<Bus> driven)
    {
        var blockers = driven.Count == 0
            ? new List<string>()
            : new List<string> { $"drives {string.Join(", ", driven.Select(b => b.BusNumber))}; assign another driver to {(driven.Count == 1 ? "that bus" : "those buses")} first" };
        var warnings = driven.Count == 0 ? new List<string>()
            : new List<string> { "Deactivating keeps the driver on their buses; they just cannot be assigned to another." };
        return new DeletionCheckDto(blockers.Count == 0, blockers, new List<string>(), warnings);
    }

    // ------------------------------------------------------------------ routes and stops

    public const string StopNotFound = "Stop not found.";
    public const string StopsNeedRoute = "This assignment has no route, so pickup and drop-off stops cannot be set.";
    public static string StopNotOnRoute(string which) => $"The {which} stop is not on this student's route.";
    public static string StopInactive(string which) => $"The {which} stop is deactivated. Choose an active stop.";

    private async Task<List<RouteStopDto>> StopDtosAsync(int routeId, CancellationToken ct)
    {
        var usage = await _repo.CountActiveStopUsageAsync(routeId, ct);
        return (await _repo.GetStopsAsync(routeId, ct)).Select(s => new RouteStopDto(s.RouteStopId, s.RouteId, s.Sequence, s.Name, s.Address,
            s.PickupTime, s.DropTime, s.Status, usage.TryGetValue(s.RouteStopId, out var n) ? n : 0)).ToList();
    }

    /// <summary>Pickup and drop-off stops must be active stops on the route the student travels on.</summary>
    private async Task<string?> CheckStopsAsync(int? routeId, int? pickupId, int? dropId, CancellationToken ct)
    {
        if (pickupId is null && dropId is null) return null;
        if (routeId is null) return StopsNeedRoute;
        foreach (var (id, which) in new[] { (pickupId, "pickup"), (dropId, "drop-off") })
        {
            if (id is not int stopId) continue;
            var stop = await _repo.GetStopAsync(stopId, ct);
            if (stop is null || stop.RouteId != routeId) return StopNotOnRoute(which);
            if (stop.Status != RecordStatus.Active) return StopInactive(which);
        }
        return null;
    }

    public async Task<Result<RouteDetailDto>> GetRouteDetailAsync(int id, CancellationToken ct = default)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result<RouteDetailDto>.Failure(RouteNotFound);
        var counts = await _repo.GetActiveAssignmentCountsAsync(ct);
        var buses = (await _repo.GetBusesAsync(ct)).Where(b => b.RouteId == id)
            .Select(b => new BusSummaryDto(b.BusId, b.BusNumber, b.RegistrationNumber, b.RouteId, route.RouteName, b.Status,
                counts.TryGetValue(b.BusId, out var n) ? n : 0)).ToList();
        var roster = Roster(await _repo.GetActiveRosterByRouteAsync(id, ct));
        return Result<RouteDetailDto>.Success(new RouteDetailDto(await RouteDtoAsync(route, ct), route.CreatedAt,
            await StopDtosAsync(id, ct), buses, roster, await RouteCheckAsync(id, ct)));
    }

    private async Task<DeletionCheckDto> RouteCheckAsync(int routeId, CancellationToken ct)
    {
        var (buses, assignments, events) = await _repo.CountRouteHistoryAsync(routeId, ct);
        var blockers = new List<string>();
        if (buses > 0) blockers.Add($"used by {buses} bus{(buses == 1 ? "" : "es")}; give {(buses == 1 ? "it" : "them")} another route first");
        if (assignments > 0) blockers.Add($"{assignments} student assignment{(assignments == 1 ? "" : "s")} (current or past)");
        if (events > 0) blockers.Add($"{events} journey event{(events == 1 ? "" : "s")}");
        var stops = (await _repo.GetStopsAsync(routeId, ct)).Count;
        var also = stops > 0 ? new List<string> { $"{stops} stop{(stops == 1 ? "" : "s")}" } : new List<string>();
        return new DeletionCheckDto(blockers.Count == 0, blockers, also, new List<string>());
    }

    public async Task<Result> UpdateRouteDetailsAsync(int id, UpdateRouteDetailsRequest r, CancellationToken ct = default, int? actorAdminId = null)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result.Failure(RouteNotFound);
        var changes = AuditDetails.Changes(("startPoint", route.StartPoint, Blank(r.StartPoint)), ("destination", route.Destination, Blank(r.Destination)));
        if (changes.Count == 0) return Result.Success();
        route.StartPoint = Blank(r.StartPoint);
        route.Destination = Blank(r.Destination);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Route", id, "Updated", $"{route.RouteName}: {route.StartPoint ?? "-"} to {route.Destination ?? "-"}", ct);
        return Result.Success();
    }

    public async Task<Result<RouteStopDto>> AddStopAsync(int routeId, RouteStopRequest r, CancellationToken ct = default, int? actorAdminId = null)
    {
        var route = await _repo.GetRouteByIdAsync(routeId, ct);
        if (route is null) return Result<RouteStopDto>.Failure(RouteNotFound);
        var existing = await _repo.GetStopsAsync(routeId, ct);
        var stop = new RouteStop
        {
            RouteId = routeId, Sequence = existing.Count == 0 ? 1 : existing.Max(s => s.Sequence) + 1, Name = r.Name.Trim(),
            Address = Blank(r.Address), PickupTime = r.PickupTime, DropTime = r.DropTime, CreatedAt = _clock.UtcNow
        };
        await _repo.AddStopAsync(stop, ct);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "RouteStop", stop.RouteStopId, "Created", $"{route.RouteName}: stop {stop.Sequence} {stop.Name}", ct);
        return Result<RouteStopDto>.Success(new RouteStopDto(stop.RouteStopId, routeId, stop.Sequence, stop.Name, stop.Address, stop.PickupTime, stop.DropTime, stop.Status, 0));
    }

    private async Task<RouteStop?> StopOfRouteAsync(int routeId, int stopId, CancellationToken ct)
    {
        var stop = await _repo.GetStopAsync(stopId, ct);
        return stop?.RouteId == routeId ? stop : null;
    }

    public async Task<Result> UpdateStopAsync(int routeId, int stopId, RouteStopRequest r, CancellationToken ct = default, int? actorAdminId = null)
    {
        var stop = await StopOfRouteAsync(routeId, stopId, ct);
        if (stop is null) return Result.Failure(StopNotFound);
        var changes = AuditDetails.Changes(("name", stop.Name, r.Name.Trim()), ("address", stop.Address, Blank(r.Address)),
            ("pickupTime", stop.PickupTime, r.PickupTime), ("dropTime", stop.DropTime, r.DropTime));
        if (changes.Count == 0) return Result.Success();
        stop.Name = r.Name.Trim();
        stop.Address = Blank(r.Address);
        stop.PickupTime = r.PickupTime;
        stop.DropTime = r.DropTime;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "RouteStop", stopId, "Updated", $"{stop.Name}: {string.Join(", ", changes.Keys)}", ct);
        return Result.Success();
    }

    public async Task<Result<string>> RemoveStopAsync(int routeId, int stopId, CancellationToken ct = default, int? actorAdminId = null)
    {
        var stop = await StopOfRouteAsync(routeId, stopId, ct);
        if (stop is null) return Result<string>.Failure(StopNotFound);
        var current = (await _repo.CountActiveStopUsageAsync(routeId, ct)).GetValueOrDefault(stopId);
        if (current > 0)
            return Result<string>.Failure($"{current} student{(current == 1 ? " boards or leaves" : "s board or leave")} at this stop. Choose other stops for them first.");

        string message;
        if (await _repo.CountStopUsageAsync(stopId, ct) > 0)
        {
            // Past assignments still point at it: keep it as history, out of use.
            stop.Status = RecordStatus.Inactive;
            message = $"Stop {stop.Name} was used before, so it was deactivated and kept as history.";
            await _repo.SaveChangesAsync(ct);
            await AuditAsync(actorAdminId, "RouteStop", stopId, "Deactivated", stop.Name, ct);
        }
        else
        {
            _repo.RemoveStop(stop);
            message = $"Stop {stop.Name} removed.";
            await _repo.SaveChangesAsync(ct);
            await AuditAsync(actorAdminId, "RouteStop", stopId, "Deleted", stop.Name, ct);
        }
        await RenumberAsync(routeId, null, ct);
        return Result<string>.Success(message);
    }

    public async Task<Result> SetStopStatusAsync(int routeId, int stopId, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null)
    {
        var stop = await StopOfRouteAsync(routeId, stopId, ct);
        if (stop is null) return Result.Failure(StopNotFound);
        if (stop.Status == status) return Result.Success();
        if (status == RecordStatus.Inactive && (await _repo.CountActiveStopUsageAsync(routeId, ct)).GetValueOrDefault(stopId) > 0)
            return Result.Failure("Students currently board or leave at this stop. Choose other stops for them first.");
        stop.Status = status;
        await _repo.SaveChangesAsync(ct);
        await RenumberAsync(routeId, null, ct);
        await AuditAsync(actorAdminId, "RouteStop", stopId, StatusAction(status), stop.Name, ct);
        return Result.Success();
    }

    public async Task<Result> ReorderStopsAsync(int routeId, ReorderStopsRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        if (await _repo.GetRouteByIdAsync(routeId, ct) is null) return Result.Failure(RouteNotFound);
        var active = (await _repo.GetStopsAsync(routeId, ct)).Where(s => s.Status == RecordStatus.Active).Select(s => s.RouteStopId).ToHashSet();
        if (request.StopIds.Count != active.Count || request.StopIds.Distinct().Count() != active.Count || !request.StopIds.All(active.Contains))
            return Result.Failure("List every active stop of this route exactly once, in the new order.");
        await RenumberAsync(routeId, request.StopIds, ct);
        await AuditAsync(actorAdminId, "Route", routeId, "StopsReordered", string.Join(" > ", request.StopIds), ct);
        return Result.Success();
    }

    /// <summary>Active stops get 1..n in <paramref name="order"/> (or their current order); deactivated stops follow.</summary>
    private async Task RenumberAsync(int routeId, IReadOnlyList<int>? order, CancellationToken ct)
    {
        var stops = await _repo.GetStopsAsync(routeId, ct);
        var activeInOrder = order is null
            ? stops.Where(s => s.Status == RecordStatus.Active).ToList()
            : order.Select(id => stops.First(s => s.RouteStopId == id)).ToList();
        var seq = 1;
        foreach (var s in activeInOrder.Concat(stops.Where(s => s.Status != RecordStatus.Active))) s.Sequence = seq++;
        await _repo.SaveChangesAsync(ct);
    }

    public async Task<LifecycleOutcome> DeleteRouteAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return LifecycleOutcome.Missing("Route");
        var check = await RouteCheckAsync(id, ct);
        if (!check.CanDelete) return LifecycleOutcome.Refused(check);
        var name = route.RouteName;
        foreach (var stop in await _repo.GetStopsAsync(id, ct)) _repo.RemoveStop(stop);
        _repo.RemoveRoute(route);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Route", id, "Deleted", $"{name} permanently deleted. Reason: {reason.Trim()}", ct);
        return LifecycleOutcome.Ok($"Route {name} permanently deleted.");
    }

    public async Task<Result> UpdateAssignmentStopsAsync(int assignmentId, UpdateAssignmentStopsRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var a = await _repo.GetAssignmentByIdAsync(assignmentId, ct);
        if (a is null) return Result.Failure(AssignmentNotFound);
        if (a.Status != RecordStatus.Active) return Result.Failure(EndedAssignmentIsHistory);
        var error = await CheckStopsAsync(a.RouteId, request.PickupStopId, request.DropStopId, ct);
        if (error is not null) return Result.Failure(error);
        if (a.PickupStopId == request.PickupStopId && a.DropStopId == request.DropStopId) return Result.Success();
        var before = $"{a.PickupStopId?.ToString() ?? "-"}/{a.DropStopId?.ToString() ?? "-"}";
        a.PickupStopId = request.PickupStopId;
        a.DropStopId = request.DropStopId;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Assignment", assignmentId, "StopsChanged",
            $"Student {a.StudentId}: stops {before} → {request.PickupStopId?.ToString() ?? "-"}/{request.DropStopId?.ToString() ?? "-"}", ct);
        return Result.Success();
    }

    public const string DuplicateEmployeeCode = "Another driver already has this driver ID.";
    public const string DuplicateRegistration = "Another bus already has this registration number.";

    public async Task<Result> UpdateDriverProfileAsync(int id, UpdateDriverProfileRequest r, CancellationToken ct = default, int? actorAdminId = null)
    {
        var d = await _repo.GetDriverByIdAsync(id, ct);
        if (d is null) return Result.Failure(DriverNotFound);
        var code = Blank(r.EmployeeCode);
        if (code is not null && await _repo.EmployeeCodeTakenAsync(code, id, ct)) return Result.Failure(DuplicateEmployeeCode);

        var address = r.Address?.ToEntity();
        var changes = AuditDetails.Changes(
            ("fullName", d.FullName, r.FullName.Trim()), ("phone", d.Phone, Blank(r.Phone)), ("licenseNumber", d.LicenseNumber, Blank(r.LicenseNumber)),
            ("employeeCode", d.EmployeeCode, code), ("email", d.Email, Blank(r.Email)),
            ("emergencyContactName", d.EmergencyContactName, Blank(r.EmergencyContactName)), ("emergencyContactPhone", d.EmergencyContactPhone, Blank(r.EmergencyContactPhone)),
            ("licenseCategory", d.LicenseCategory, Blank(r.LicenseCategory)), ("licenseExpiresOn", d.LicenseExpiresOn, r.LicenseExpiresOn), ("joinedOn", d.JoinedOn, r.JoinedOn));
        var addressParts = AuditDetails.ChangedAddressParts(d.Address, address);
        if (changes.Count == 0 && addressParts.Count == 0) return Result.Success();

        d.FullName = r.FullName.Trim();
        d.Phone = Blank(r.Phone);
        d.LicenseNumber = Blank(r.LicenseNumber);
        d.EmployeeCode = code;
        d.Email = Blank(r.Email);
        d.Address = address;
        d.EmergencyContactName = Blank(r.EmergencyContactName);
        d.EmergencyContactPhone = Blank(r.EmergencyContactPhone);
        d.LicenseCategory = Blank(r.LicenseCategory);
        d.LicenseExpiresOn = r.LicenseExpiresOn;
        d.JoinedOn = r.JoinedOn;
        if (!await _repo.TrySaveChangesAsync(ct)) return Result.Failure(DuplicateEmployeeCode);
        var fields = changes.Keys.Concat(addressParts.Count > 0 ? new[] { "address" } : Array.Empty<string>());
        await AuditAsync(actorAdminId, "Driver", id, "ProfileUpdated", $"{d.FullName}: {string.Join(", ", fields)}", ct);
        return Result.Success();
    }

    public async Task<LifecycleOutcome> DeleteDriverAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null)
    {
        var d = await _repo.GetDriverByIdAsync(id, ct);
        if (d is null) return LifecycleOutcome.Missing("Driver");
        var check = DriverCheck((await _repo.GetBusesAsync(ct)).Where(b => b.DriverId == id).ToList());
        if (!check.CanDelete) return LifecycleOutcome.Refused(check);
        var name = d.FullName;
        _repo.RemoveDriver(d);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Driver", id, "Deleted", $"{name} permanently deleted. Reason: {reason.Trim()}", ct);
        return LifecycleOutcome.Ok($"Driver {name} permanently deleted.");
    }

    // ------------------------------------------------------------------ bus details

    public async Task<Result<BusDetailDto>> GetBusDetailAsync(int id, CancellationToken ct = default)
    {
        var b = (await _repo.GetBusesAsync(ct)).FirstOrDefault(x => x.BusId == id);
        if (b is null) return Result<BusDetailDto>.Failure(BusNotFound);
        var roster = Roster(await _repo.GetActiveRosterAsync(new[] { id }, ct));
        var dto = new BusDto(b.BusId, b.BusNumber, b.Capacity, b.Driver?.FullName, b.Route?.RouteName, b.Status, b.DriverId, b.RouteId,
            roster.Count, b.RegistrationNumber, b.Manufacturer, b.Model);
        var driver = b.Driver is { } d
            ? new DriverSummaryDto(d.DriverId, d.FullName, d.Phone, d.Email, d.LicenseNumber, d.LicenseCategory, d.LicenseExpiresOn,
                LicenceRules.State(d.LicenseExpiresOn, _clock.Today), d.Status)
            : null;
        var route = b.Route is { } r
            ? new RouteSummaryDto(r.RouteId, r.RouteName, r.Description, r.Status, r.StartPoint, r.Destination, await StopDtosAsync(r.RouteId, ct))
            : null;
        return Result<BusDetailDto>.Success(new BusDetailDto(dto, b.CreatedAt, driver, route, roster, await BusCheckAsync(b, roster.Count, ct)));
    }

    private async Task<DeletionCheckDto> BusCheckAsync(Bus b, int activeStudents, CancellationToken ct)
    {
        var (assignments, events) = await _repo.CountBusHistoryAsync(b.BusId, ct);
        var blockers = new List<string>();
        if (assignments > 0) blockers.Add($"{assignments} student assignment{(assignments == 1 ? "" : "s")} (current or past)");
        if (events > 0) blockers.Add($"{events} journey event{(events == 1 ? "" : "s")}");
        var warnings = new List<string>();
        if (activeStudents > 0)
            warnings.Add($"{activeStudents} student{(activeStudents == 1 ? " is" : "s are")} on this bus. Taking it out of service keeps them assigned until you move them.");
        return new DeletionCheckDto(blockers.Count == 0, blockers, new List<string>(), warnings);
    }

    public async Task<Result> UpdateBusDetailsAsync(int id, UpdateBusDetailsRequest r, CancellationToken ct = default, int? actorAdminId = null)
    {
        var b = await _repo.GetBusByIdAsync(id, ct);
        if (b is null) return Result.Failure(BusNotFound);
        var registration = Blank(r.RegistrationNumber)?.ToUpperInvariant();
        if (registration is not null && await _repo.RegistrationTakenAsync(registration, id, ct)) return Result.Failure(DuplicateRegistration);
        var changes = AuditDetails.Changes(("registrationNumber", b.RegistrationNumber, registration),
            ("manufacturer", b.Manufacturer, Blank(r.Manufacturer)), ("model", b.Model, Blank(r.Model)));
        if (changes.Count == 0) return Result.Success();
        b.RegistrationNumber = registration;
        b.Manufacturer = Blank(r.Manufacturer);
        b.Model = Blank(r.Model);
        if (!await _repo.TrySaveChangesAsync(ct)) return Result.Failure(DuplicateRegistration);
        await AuditAsync(actorAdminId, "Bus", id, "DetailsUpdated",
            $"{b.BusNumber}: " + string.Join(", ", changes.Select(c => c.Key)), ct);
        return Result.Success();
    }

    public async Task<LifecycleOutcome> DeleteBusAsync(int id, string reason, CancellationToken ct = default, int? actorAdminId = null)
    {
        var b = await _repo.GetBusByIdAsync(id, ct);
        if (b is null) return LifecycleOutcome.Missing("Bus");
        var check = await BusCheckAsync(b, 0, ct);
        if (!check.CanDelete) return LifecycleOutcome.Refused(check);
        var number = b.BusNumber;
        _repo.RemoveBus(b);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Bus", id, "Deleted", $"{number} permanently deleted. Reason: {reason.Trim()}", ct);
        return LifecycleOutcome.Ok($"Bus {number} permanently deleted.");
    }

    public async Task<DriverDto> AddDriverAsync(CreateDriverRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var driver = new Driver
        {
            FullName = request.FullName.Trim(), Phone = Blank(request.Phone), LicenseNumber = Blank(request.LicenseNumber),
            CreatedAt = _clock.UtcNow
        };
        await _repo.AddDriverAsync(driver, ct);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Driver", driver.DriverId, "Created", $"{driver.FullName}{(driver.LicenseNumber is null ? "" : $", licence {driver.LicenseNumber}")}", ct);
        return new DriverDto(driver.DriverId, driver.FullName, driver.Phone, driver.LicenseNumber, driver.Status);
    }

    public async Task<Result> UpdateDriverAsync(int id, UpdateDriverRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var driver = await _repo.GetDriverByIdAsync(id, ct);
        if (driver is null) return Result.Failure(DriverNotFound);
        var before = $"{driver.FullName} / {driver.Phone ?? "-"} / {driver.LicenseNumber ?? "-"}";
        driver.FullName = request.FullName.Trim();
        driver.Phone = Blank(request.Phone);
        driver.LicenseNumber = Blank(request.LicenseNumber);
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Driver", id, "Updated", $"{before} → {driver.FullName} / {driver.Phone ?? "-"} / {driver.LicenseNumber ?? "-"}", ct);
        return Result.Success();
    }

    /// <summary>Deactivating keeps the driver on every bus they drive; they just cannot be newly assigned.</summary>
    public async Task<Result> SetDriverStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null)
    {
        var driver = await _repo.GetDriverByIdAsync(id, ct);
        if (driver is null) return Result.Failure(DriverNotFound);
        if (driver.Status == status) return Result.Success();
        driver.Status = status;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Driver", id, StatusAction(status), driver.FullName, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ routes

    public async Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default)
    {
        var list = new List<RouteDto>();
        foreach (var r in await _repo.GetRoutesAsync(ct))
            list.Add(await RouteDtoAsync(r, ct));
        return list;
    }

    private async Task<RouteDto> RouteDtoAsync(Route r, CancellationToken ct) =>
        new(r.RouteId, r.RouteName, r.Description, r.Status, r.StartPoint, r.Destination,
            (await _repo.GetStopsAsync(r.RouteId, ct)).Count(s => s.Status == RecordStatus.Active));

    public async Task<Result<RouteDto>> AddRouteAsync(CreateRouteRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var name = request.RouteName.Trim();
        if ((await _repo.GetRoutesAsync(ct)).Any(r => SameText(r.RouteName, name)))
            return Result<RouteDto>.Failure(DuplicateRouteName);

        var route = new Route { RouteName = name, Description = Blank(request.Description), CreatedAt = _clock.UtcNow };
        await _repo.AddRouteAsync(route, ct);
        if (!await _repo.TrySaveChangesAsync(ct)) return Result<RouteDto>.Failure(DuplicateRouteName);
        await AuditAsync(actorAdminId, "Route", route.RouteId, "Created", route.RouteName, ct);
        return Result<RouteDto>.Success(new RouteDto(route.RouteId, route.RouteName, route.Description, route.Status));
    }

    public async Task<Result> UpdateRouteAsync(int id, UpdateRouteRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result.Failure(RouteNotFound);
        var name = request.RouteName.Trim();
        if ((await _repo.GetRoutesAsync(ct)).Any(r => r.RouteId != id && SameText(r.RouteName, name)))
            return Result.Failure(DuplicateRouteName);
        var before = route.RouteName;
        route.RouteName = name;
        route.Description = Blank(request.Description);
        if (!await _repo.TrySaveChangesAsync(ct)) return Result.Failure(DuplicateRouteName);
        await AuditAsync(actorAdminId, "Route", id, "Updated", before == name ? name : $"{before} → {name}", ct);
        return Result.Success();
    }

    /// <summary>Deactivating keeps the route on buses and assignments that have it; it just cannot be newly assigned.</summary>
    public async Task<Result> SetRouteStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result.Failure(RouteNotFound);
        if (route.Status == status) return Result.Success();
        route.Status = status;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Route", id, StatusAction(status), route.RouteName, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ buses

    public async Task<List<BusDto>> GetBusesAsync(CancellationToken ct = default)
    {
        var counts = await _repo.GetActiveAssignmentCountsAsync(ct);
        return (await _repo.GetBusesAsync(ct)).Select(b => new BusDto(
            b.BusId, b.BusNumber, b.Capacity, b.Driver?.FullName, b.Route?.RouteName, b.Status, b.DriverId, b.RouteId,
            counts.TryGetValue(b.BusId, out var n) ? n : 0, b.RegistrationNumber, b.Manufacturer, b.Model)).ToList();
    }

    public async Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        var number = request.BusNumber.Trim();
        if ((await _repo.GetBusesAsync(ct)).Any(b => SameText(b.BusNumber, number)))
            return Result<BusDto>.Failure(DuplicateBusNumber);

        var (driver, route, error) = await ResolveCrewAsync(request.DriverId, request.RouteId, null, null, ct);
        if (error is not null) return Result<BusDto>.Failure(error);

        var bus = new Bus { BusNumber = number, Capacity = request.Capacity, DriverId = request.DriverId, RouteId = request.RouteId, CreatedAt = _clock.UtcNow };
        await _repo.AddBusAsync(bus, ct);
        if (!await _repo.TrySaveChangesAsync(ct)) return Result<BusDto>.Failure(DuplicateBusNumber);
        await AuditAsync(actorAdminId, "Bus", bus.BusId, "Created",
            $"{bus.BusNumber}, {bus.Capacity} seats, driver {driver?.FullName ?? "none"}, route {route?.RouteName ?? "none"}", ct);
        return Result<BusDto>.Success(new BusDto(bus.BusId, bus.BusNumber, bus.Capacity, driver?.FullName, route?.RouteName, bus.Status, bus.DriverId, bus.RouteId, 0));
    }

    /// <summary>
    /// Edits a bus. Its driver and route may only change to active ones (keeping the current ones
    /// is always allowed), and capacity may not drop below the students currently assigned. The
    /// bus row is locked so a concurrent assignment cannot slip in between the count and the save.
    /// Journey history is untouched: events keep the bus and route they were recorded with.
    /// </summary>
    public async Task<Result> UpdateBusAsync(int id, UpdateBusRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        if (await _repo.GetBusByIdAsync(id, ct) is null) return Result.Failure(BusNotFound);
        var number = request.BusNumber.Trim();
        if ((await _repo.GetBusesAsync(ct)).Any(b => b.BusId != id && SameText(b.BusNumber, number)))
            return Result.Failure(DuplicateBusNumber);

        string? detail = null;
        var result = await _repo.RunLockedAsync(Array.Empty<int>(), new[] { id }, async lockCt =>
        {
            var bus = await _repo.GetBusByIdAsync(id, lockCt);
            if (bus is null) return Result.Failure(BusNotFound);

            var (_, _, error) = await ResolveCrewAsync(request.DriverId, request.RouteId, bus.DriverId, bus.RouteId, lockCt);
            if (error is not null) return Result.Failure(error);

            var active = await _repo.CountActiveAssignmentsAsync(id, lockCt);
            if (request.Capacity < active) return Result.Failure(CapacityBelowOccupancy(request.Capacity, active));

            detail = $"{bus.BusNumber}/{bus.Capacity} seats/driver {bus.DriverId?.ToString() ?? "-"}/route {bus.RouteId?.ToString() ?? "-"}"
                   + $" → {number}/{request.Capacity} seats/driver {request.DriverId?.ToString() ?? "-"}/route {request.RouteId?.ToString() ?? "-"}";
            bus.BusNumber = number;
            bus.Capacity = request.Capacity;
            bus.DriverId = request.DriverId;
            bus.RouteId = request.RouteId;
            return await _repo.TrySaveChangesAsync(lockCt) ? Result.Success() : Result.Failure(DuplicateBusNumber);
        }, ct);

        if (result.Succeeded) await AuditAsync(actorAdminId, "Bus", id, "Updated", detail, ct);
        return result;
    }

    /// <summary>Taking a bus out of service blocks new assignments; current ones stay until changed.</summary>
    public async Task<Result> SetBusStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null)
    {
        var bus = await _repo.GetBusByIdAsync(id, ct);
        if (bus is null) return Result.Failure(BusNotFound);
        var target = status == RecordStatus.Active ? BusStatus.Active : BusStatus.Inactive;
        if (bus.Status == target) return Result.Success();
        bus.Status = target;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Bus", id, target == BusStatus.Active ? "ReturnedToService" : "TakenOutOfService", bus.BusNumber, ct);
        return Result.Success();
    }

    /// <summary>
    /// Sets a bus's state, including UnderMaintenance and OutOfService. Like deactivation, a non-active bus keeps
    /// its students but takes no new ones; the change and its reason are audited.
    /// </summary>
    public async Task<Result> SetBusOperationalStatusAsync(int id, BusStatus status, string? reason, CancellationToken ct = default, int? actorAdminId = null)
    {
        var bus = await _repo.GetBusByIdAsync(id, ct);
        if (bus is null) return Result.Failure(BusNotFound);
        if (bus.Status == status) return Result.Success();
        var from = bus.Status;
        bus.Status = status;
        await _repo.SaveChangesAsync(ct);
        var text = reason is { Length: > 0 } ? $"{bus.BusNumber}: {from} → {status} ({reason.Trim()})" : $"{bus.BusNumber}: {from} → {status}";
        await AuditAsync(actorAdminId, "Bus", id, status == BusStatus.Active ? "ReturnedToService" : $"Set{status}", text, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ assignments

    public async Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default) =>
        (await _repo.GetAssignmentsAsync(ct)).Select(a => new StudentBusAssignmentDto(
            a.Id, a.StudentId, a.Student.FullName, a.Bus.BusNumber, a.Route?.RouteName, a.Status, a.AssignedAt,
            a.BusId, a.RouteId, a.EndedAt, a.PickupStopId, a.PickupStop?.Name, a.DropStopId, a.DropStop?.Name)).ToList();

    /// <summary>
    /// One active assignment per student. Under row locks on the student and the target bus,
    /// in one transaction: the target bus must be in service and have a free seat, the route is
    /// the bus's own (or an allowed active override), the current assignment (if any) is ended
    /// and the new one created. The database's unique key on active assignments backs this up.
    /// </summary>
    public async Task<Result> AddAssignmentAsync(CreateStudentBusAssignmentRequest request, CancellationToken ct = default, int? actorAdminId = null)
    {
        if (await _repo.GetStudentAsync(request.StudentId, ct) is null) return Result.Failure(StudentNotFound);
        if (await _repo.GetBusByIdAsync(request.BusId, ct) is null) return Result.Failure(BusNotFound);

        int? endedId = null, createdId = null;
        string? detail = null;
        var result = await _repo.RunLockedAsync(new[] { request.StudentId }, new[] { request.BusId }, async lockCt =>
        {
            var bus = await _repo.GetBusByIdAsync(request.BusId, lockCt);
            if (bus is null) return Result.Failure(BusNotFound);
            if (bus.Status != BusStatus.Active) return Result.Failure(BusOutOfService);

            var (routeId, routeError) = await ResolveAssignmentRouteAsync(bus, request.RouteId, lockCt);
            if (routeError is not null) return Result.Failure(routeError);

            var current = await _repo.GetActiveAssignmentForStudentAsync(request.StudentId, lockCt);
            if (current is not null && current.BusId == bus.BusId && current.RouteId == routeId)
                return Result.Failure(AlreadyOnThisBus);

            var stopError = await CheckStopsAsync(routeId, request.PickupStopId, request.DropStopId, lockCt);
            if (stopError is not null) return Result.Failure(stopError);

            // A move within the same bus (route change only) does not need a free seat.
            var taken = await _repo.CountActiveAssignmentsAsync(bus.BusId, lockCt);
            if (current?.BusId != bus.BusId && taken >= bus.Capacity)
                return Result.Failure(BusFull(bus.BusNumber, bus.Capacity));

            var now = _clock.UtcNow;
            if (current is not null)
            {
                current.Status = RecordStatus.Inactive;
                current.EndedAt = now;
                endedId = current.Id;
                // Saved first so the database's one-active-per-student key never sees two active rows.
                if (!await _repo.TrySaveChangesAsync(lockCt)) return Result.Failure(AssignmentChangedConcurrently);
            }

            var assignment = new StudentBusAssignment
            {
                StudentId = request.StudentId, BusId = bus.BusId, RouteId = routeId, Status = RecordStatus.Active, AssignedAt = now,
                PickupStopId = request.PickupStopId, DropStopId = request.DropStopId
            };
            await _repo.AddAssignmentAsync(assignment, lockCt);
            if (!await _repo.TrySaveChangesAsync(lockCt)) return Result.Failure(AssignmentChangedConcurrently);
            createdId = assignment.Id;
            detail = $"Student {request.StudentId} → bus {bus.BusNumber}, route {routeId?.ToString() ?? "none"}"
                   + (current is null ? "" : $" (ended assignment {current.Id} on bus {current.BusId})");
            return Result.Success();
        }, ct);

        if (result.Succeeded)
            await AuditAsync(actorAdminId, "Assignment", createdId!.Value, endedId is null ? "Assigned" : "Reassigned", detail, ct);
        return result;
    }

    public async Task<Result> SetAssignmentStatusAsync(int id, RecordStatus status, CancellationToken ct = default, int? actorAdminId = null)
    {
        var assignment = await _repo.GetAssignmentByIdAsync(id, ct);
        if (assignment is null) return Result.Failure(AssignmentNotFound);
        if (assignment.Status == status) return Result.Success();
        if (status == RecordStatus.Active) return Result.Failure(EndedAssignmentIsHistory);

        var result = await _repo.RunLockedAsync(new[] { assignment.StudentId }, Array.Empty<int>(), async lockCt =>
        {
            var a = await _repo.GetAssignmentByIdAsync(id, lockCt);
            if (a is null) return Result.Failure(AssignmentNotFound);
            if (a.Status == RecordStatus.Inactive) return Result.Success();
            a.Status = RecordStatus.Inactive;
            a.EndedAt = _clock.UtcNow;
            await _repo.SaveChangesAsync(lockCt);
            return Result.Success();
        }, ct);

        if (result.Succeeded)
            await AuditAsync(actorAdminId, "Assignment", id, "Ended", $"Student {assignment.StudentId}, bus {assignment.BusId}", ct);
        return result;
    }

    // ------------------------------------------------------------------ audit

    public async Task<List<TransportAuditDto>> GetAuditAsync(int take, CancellationToken ct = default) =>
        (await _repo.GetAuditAsync(Math.Clamp(take, 1, 500), ct))
            .Select(x => new TransportAuditDto(x.Entry.Id, x.Entry.OccurredAt, x.Entry.AdminId, x.AdminName,
                x.Entry.EntityType, x.Entry.EntityId, x.Entry.Action, x.Entry.Detail))
            .ToList();

    private async Task AuditAsync(int? adminId, string entityType, int entityId, string action, string? detail, CancellationToken ct)
    {
        await _repo.AddAuditAsync(new TransportAuditEntry
        {
            OccurredAt = _clock.UtcNow, AdminId = adminId, EntityType = entityType, EntityId = entityId, Action = action,
            Detail = detail is { Length: > 1000 } ? detail[..1000] : detail
        }, ct);
        // Mirrored into the general audit trail, saved together with the transport entry.
        _audit.Record(new AuditEvent(entityType + action, entityType, entityId, $"{entityType} {entityId}: {action}")
        { Details = detail is null ? null : new { detail } });
        await _repo.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// A bus's driver and route: each must exist, and must be active unless it is the one the
    /// bus already has (deactivation never strips existing relationships).
    /// </summary>
    private async Task<(Driver? Driver, Route? Route, string? Error)> ResolveCrewAsync(
        int? driverId, int? routeId, int? currentDriverId, int? currentRouteId, CancellationToken ct)
    {
        Driver? driver = null;
        if (driverId is int d)
        {
            driver = await _repo.GetDriverByIdAsync(d, ct);
            if (driver is null) return (null, null, DriverNotFound);
            if (driver.Status != RecordStatus.Active && d != currentDriverId) return (null, null, DriverInactive);
        }
        Route? route = null;
        if (routeId is int r)
        {
            route = await _repo.GetRouteByIdAsync(r, ct);
            if (route is null) return (null, null, RouteNotFound);
            if (route.Status != RecordStatus.Active && r != currentRouteId) return (null, null, RouteInactive);
        }
        return (driver, route, null);
    }

    /// <summary>The assignment's route: the bus's own by default; another only if the school allows overrides and the route is active.</summary>
    private async Task<(int? RouteId, string? Error)> ResolveAssignmentRouteAsync(Bus bus, int? requested, CancellationToken ct)
    {
        if (requested is null || requested == bus.RouteId) return (bus.RouteId, null);
        if (!_options.AllowRouteOverride) return (null, RouteOverrideNotAllowed);
        var route = await _repo.GetRouteByIdAsync(requested.Value, ct);
        if (route is null) return (null, RouteNotFound);
        if (route.Status != RecordStatus.Active) return (null, RouteInactive);
        return (route.RouteId, null);
    }

    private static string StatusAction(RecordStatus status) => status == RecordStatus.Active ? "Activated" : "Deactivated";
    private static bool SameText(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
