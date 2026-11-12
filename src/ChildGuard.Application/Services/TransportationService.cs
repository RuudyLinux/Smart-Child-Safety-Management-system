using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class TransportationService : ITransportationService
{
    private readonly ITransportationRepository _repo;
    public TransportationService(ITransportationRepository repo) => _repo = repo;

    public async Task<List<DriverDto>> GetDriversAsync(CancellationToken ct = default) =>
        (await _repo.GetDriversAsync(ct)).Select(d => new DriverDto(d.DriverId, d.FullName, d.Phone, d.LicenseNumber, d.Status)).ToList();

    public async Task<DriverDto> AddDriverAsync(CreateDriverRequest request, CancellationToken ct = default)
    {
        var driver = new Driver { FullName = request.FullName, Phone = request.Phone, LicenseNumber = request.LicenseNumber };
        await _repo.AddDriverAsync(driver, ct);
        await _repo.SaveChangesAsync(ct);
        return new DriverDto(driver.DriverId, driver.FullName, driver.Phone, driver.LicenseNumber, driver.Status);
    }

    public async Task<List<RouteDto>> GetRoutesAsync(CancellationToken ct = default) =>
        (await _repo.GetRoutesAsync(ct)).Select(r => new RouteDto(r.RouteId, r.RouteName, r.Description, r.Status)).ToList();

    public async Task<Result<RouteDto>> AddRouteAsync(CreateRouteRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.GetRoutesAsync(ct);
        if (existing.Any(r => string.Equals(r.RouteName, request.RouteName, StringComparison.OrdinalIgnoreCase)))
            return Result<RouteDto>.Failure("A route with this name already exists.");

        var route = new Route { RouteName = request.RouteName, Description = request.Description };
        await _repo.AddRouteAsync(route, ct);
        await _repo.SaveChangesAsync(ct);
        return Result<RouteDto>.Success(new RouteDto(route.RouteId, route.RouteName, route.Description, route.Status));
    }

    public async Task<List<BusDto>> GetBusesAsync(CancellationToken ct = default) =>
        (await _repo.GetBusesAsync(ct)).Select(b => new BusDto(
            b.BusId, b.BusNumber, b.Capacity, b.Driver?.FullName, b.Route?.RouteName, b.Status, b.DriverId, b.RouteId)).ToList();

    public async Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default)
    {
        var existingBuses = await _repo.GetBusesAsync(ct);
        if (existingBuses.Any(b => string.Equals(b.BusNumber, request.BusNumber, StringComparison.OrdinalIgnoreCase)))
            return Result<BusDto>.Failure("A bus with this number already exists.");

        Driver? driver = null;
        if (request.DriverId is int driverId)
        {
            driver = (await _repo.GetDriversAsync(ct)).FirstOrDefault(d => d.DriverId == driverId);
            if (driver is null) return Result<BusDto>.Failure("Driver not found.");
        }

        Route? route = null;
        if (request.RouteId is int routeId)
        {
            route = (await _repo.GetRoutesAsync(ct)).FirstOrDefault(r => r.RouteId == routeId);
            if (route is null) return Result<BusDto>.Failure("Route not found.");
        }

        var bus = new Bus
        {
            BusNumber = request.BusNumber,
            Capacity = request.Capacity,
            DriverId = request.DriverId,
            RouteId = request.RouteId
        };
        await _repo.AddBusAsync(bus, ct);
        await _repo.SaveChangesAsync(ct);
        return Result<BusDto>.Success(new BusDto(bus.BusId, bus.BusNumber, bus.Capacity, driver?.FullName, route?.RouteName, bus.Status, bus.DriverId, bus.RouteId));
    }

    public async Task<List<StudentBusAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default) =>
        (await _repo.GetAssignmentsAsync(ct)).Select(a => new StudentBusAssignmentDto(
            a.Id, a.StudentId, a.Student.FullName, a.Bus.BusNumber, a.Route?.RouteName, a.Status, a.AssignedAt)).ToList();

    public async Task<Result> AddAssignmentAsync(CreateStudentBusAssignmentRequest request, CancellationToken ct = default)
    {
        var student = await _repo.GetStudentAsync(request.StudentId, ct);
        if (student is null) return Result.Failure("Student not found.");

        var bus = await _repo.GetBusByIdAsync(request.BusId, ct);
        if (bus is null) return Result.Failure("Bus not found.");

        if (request.RouteId is int routeId &&
            (await _repo.GetRoutesAsync(ct)).All(r => r.RouteId != routeId))
            return Result.Failure("Route not found.");

        // A student should have exactly one active bus assignment — reassigning
        // to a new bus supersedes whatever was active before, rather than piling
        // up duplicate "current" assignments the safety board has to pick between.
        var existingAssignments = await _repo.GetAssignmentsAsync(ct);
        foreach (var prior in existingAssignments.Where(a => a.StudentId == request.StudentId && a.Status == RecordStatus.Active))
            prior.Status = RecordStatus.Inactive;

        await _repo.AddAssignmentAsync(new StudentBusAssignment
        {
            StudentId = request.StudentId,
            BusId = request.BusId,
            RouteId = request.RouteId
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public const string DriverNotFound = "Driver not found.";
    public const string RouteNotFound = "Route not found.";
    public const string BusNotFound = "Bus not found.";
    public const string AssignmentNotFound = "Assignment not found.";

    public async Task<Result> UpdateDriverAsync(int id, UpdateDriverRequest request, CancellationToken ct = default)
    {
        var driver = await _repo.GetDriverByIdAsync(id, ct);
        if (driver is null) return Result.Failure(DriverNotFound);
        driver.FullName = request.FullName.Trim();
        driver.Phone = Blank(request.Phone);
        driver.LicenseNumber = Blank(request.LicenseNumber);
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetDriverStatusAsync(int id, RecordStatus status, CancellationToken ct = default)
    {
        var driver = await _repo.GetDriverByIdAsync(id, ct);
        if (driver is null) return Result.Failure(DriverNotFound);
        driver.Status = status;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateRouteAsync(int id, UpdateRouteRequest request, CancellationToken ct = default)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result.Failure(RouteNotFound);
        var name = request.RouteName.Trim();
        if ((await _repo.GetRoutesAsync(ct)).Any(r => r.RouteId != id && string.Equals(r.RouteName, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure("A route with this name already exists.");
        route.RouteName = name;
        route.Description = Blank(request.Description);
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetRouteStatusAsync(int id, RecordStatus status, CancellationToken ct = default)
    {
        var route = await _repo.GetRouteByIdAsync(id, ct);
        if (route is null) return Result.Failure(RouteNotFound);
        route.Status = status;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateBusAsync(int id, UpdateBusRequest request, CancellationToken ct = default)
    {
        var bus = await _repo.GetBusByIdAsync(id, ct);
        if (bus is null) return Result.Failure(BusNotFound);
        var number = request.BusNumber.Trim();
        if ((await _repo.GetBusesAsync(ct)).Any(b => b.BusId != id && string.Equals(b.BusNumber, number, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure("A bus with this number already exists.");
        if (request.DriverId is int driverId && await _repo.GetDriverByIdAsync(driverId, ct) is null)
            return Result.Failure(DriverNotFound);
        if (request.RouteId is int routeId && await _repo.GetRouteByIdAsync(routeId, ct) is null)
            return Result.Failure(RouteNotFound);

        bus.BusNumber = number;
        bus.Capacity = request.Capacity;
        bus.DriverId = request.DriverId;
        bus.RouteId = request.RouteId;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetBusStatusAsync(int id, RecordStatus status, CancellationToken ct = default)
    {
        var bus = await _repo.GetBusByIdAsync(id, ct);
        if (bus is null) return Result.Failure(BusNotFound);
        bus.Status = status;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetAssignmentStatusAsync(int id, RecordStatus status, CancellationToken ct = default)
    {
        var assignment = await _repo.GetAssignmentByIdAsync(id, ct);
        if (assignment is null) return Result.Failure(AssignmentNotFound);
        assignment.Status = status;
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
