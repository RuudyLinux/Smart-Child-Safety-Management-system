using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ChildGuard.Infrastructure.Repositories;

public class TransportationRepository : ITransportationRepository
{
    private readonly ChildGuardDbContext _db;
    public TransportationRepository(ChildGuardDbContext db) => _db = db;

    public Task<List<Driver>> GetDriversAsync(CancellationToken ct = default) =>
        _db.Drivers.OrderBy(d => d.FullName).ToListAsync(ct);

    public async Task AddDriverAsync(Driver driver, CancellationToken ct = default) =>
        await _db.Drivers.AddAsync(driver, ct);

    public Task<List<Route>> GetRoutesAsync(CancellationToken ct = default) =>
        _db.Routes.OrderBy(r => r.RouteName).ToListAsync(ct);

    public async Task AddRouteAsync(Route route, CancellationToken ct = default) =>
        await _db.Routes.AddAsync(route, ct);

    public Task<List<Bus>> GetBusesAsync(CancellationToken ct = default) =>
        _db.Buses.Include(b => b.Driver).Include(b => b.Route).OrderBy(b => b.BusNumber).ToListAsync(ct);

    public Task<Bus?> GetBusByIdAsync(int id, CancellationToken ct = default) =>
        _db.Buses.FirstOrDefaultAsync(b => b.BusId == id, ct);

    public async Task AddBusAsync(Bus bus, CancellationToken ct = default) =>
        await _db.Buses.AddAsync(bus, ct);

    public Task<List<StudentBusAssignment>> GetAssignmentsAsync(CancellationToken ct = default) =>
        _db.StudentBusAssignments
            .Include(a => a.Student)
            .Include(a => a.Bus)
            .Include(a => a.Route)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync(ct);

    public Task<Student?> GetStudentAsync(int studentId, CancellationToken ct = default) =>
        _db.Students.FirstOrDefaultAsync(s => s.StudentId == studentId, ct);

    public async Task AddAssignmentAsync(StudentBusAssignment assignment, CancellationToken ct = default) =>
        await _db.StudentBusAssignments.AddAsync(assignment, ct);

    public Task<Driver?> GetDriverByIdAsync(int id, CancellationToken ct = default) =>
        _db.Drivers.FirstOrDefaultAsync(d => d.DriverId == id, ct);

    public Task<Route?> GetRouteByIdAsync(int id, CancellationToken ct = default) =>
        _db.Routes.FirstOrDefaultAsync(r => r.RouteId == id, ct);

    public Task<StudentBusAssignment?> GetAssignmentByIdAsync(int id, CancellationToken ct = default) =>
        _db.StudentBusAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
