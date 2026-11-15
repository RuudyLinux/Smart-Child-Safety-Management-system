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

    public TransportationService(ITransportationRepository repo, SchoolClock clock, TransportOptions options)
    {
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

    public async Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default) =>
        (await _repo.GetDriversAsync(ct)).Select(d => new DriverDto(d.DriverId, d.FullName, d.Phone, d.LicenseNumber, d.Status)).ToList();

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

    public async Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default) =>
        (await _repo.GetRoutesAsync(ct)).Select(r => new RouteDto(r.RouteId, r.RouteName, r.Description, r.Status)).ToList();

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
            counts.TryGetValue(b.BusId, out var n) ? n : 0)).ToList();
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
        if (bus.Status == status) return Result.Success();
        bus.Status = status;
        await _repo.SaveChangesAsync(ct);
        await AuditAsync(actorAdminId, "Bus", id, status == RecordStatus.Active ? "ReturnedToService" : "TakenOutOfService", bus.BusNumber, ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ assignments

    public async Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default) =>
        (await _repo.GetAssignmentsAsync(ct)).Select(a => new StudentBusAssignmentDto(
            a.Id, a.StudentId, a.Student.FullName, a.Bus.BusNumber, a.Route?.RouteName, a.Status, a.AssignedAt,
            a.BusId, a.RouteId, a.EndedAt)).ToList();

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
            if (bus.Status != RecordStatus.Active) return Result.Failure(BusOutOfService);

            var (routeId, routeError) = await ResolveAssignmentRouteAsync(bus, request.RouteId, lockCt);
            if (routeError is not null) return Result.Failure(routeError);

            var current = await _repo.GetActiveAssignmentForStudentAsync(request.StudentId, lockCt);
            if (current is not null && current.BusId == bus.BusId && current.RouteId == routeId)
                return Result.Failure(AlreadyOnThisBus);

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
                StudentId = request.StudentId, BusId = bus.BusId, RouteId = routeId, Status = RecordStatus.Active, AssignedAt = now
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
