using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Interfaces;

public interface ITransportationRepository
{
    Task<List<Driver>> GetDriversAsync(CancellationToken ct = default);
    Task AddDriverAsync(Driver driver, CancellationToken ct = default);

    Task<List<Route>> GetRoutesAsync(CancellationToken ct = default);
    Task AddRouteAsync(Route route, CancellationToken ct = default);

    Task<List<Bus>> GetBusesAsync(CancellationToken ct = default);
    Task<Bus?> GetBusByIdAsync(int id, CancellationToken ct = default);
    Task AddBusAsync(Bus bus, CancellationToken ct = default);

    Task<List<StudentBusAssignment>> GetAssignmentsAsync(CancellationToken ct = default);
    Task<Student?> GetStudentAsync(int studentId, CancellationToken ct = default);
    Task AddAssignmentAsync(StudentBusAssignment assignment, CancellationToken ct = default);

    Task<Driver?> GetDriverByIdAsync(int id, CancellationToken ct = default);
    Task<Route?> GetRouteByIdAsync(int id, CancellationToken ct = default);
    Task<StudentBusAssignment?> GetAssignmentByIdAsync(int id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
