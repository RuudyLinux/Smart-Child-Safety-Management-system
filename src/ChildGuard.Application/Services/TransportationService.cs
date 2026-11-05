using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

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
            b.BusId, b.BusNumber, b.Capacity, b.Driver?.FullName, b.Route?.RouteName, b.Status)).ToList();

    public async Task<Result<BusDto>> AddBusAsync(CreateBusRequest request, CancellationToken ct = default)
    {
        var existing = await _repo.GetBusesAsync(ct);
        if (existing.Any(b => string.Equals(b.BusNumber, request.BusNumber, StringComparison.OrdinalIgnoreCase)))
            return Result<BusDto>.Failure("A bus with this number already exists.");

        var bus = new Bus
        {
            BusNumber = request.BusNumber,
            Capacity = request.Capacity,
            DriverId = request.DriverId,
            RouteId = request.RouteId
        };
        await _repo.AddBusAsync(bus, ct);
        await _repo.SaveChangesAsync(ct);
        return Result<BusDto>.Success(new BusDto(bus.BusId, bus.BusNumber, bus.Capacity, null, null, bus.Status));
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

        await _repo.AddAssignmentAsync(new StudentBusAssignment
        {
            StudentId = request.StudentId,
            BusId = request.BusId,
            RouteId = request.RouteId
        }, ct);
        await _repo.SaveChangesAsync(ct);
        return Result.Success();
    }
}
