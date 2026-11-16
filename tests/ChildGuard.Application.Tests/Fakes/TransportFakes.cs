using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Tests.Fakes;

/// <summary>
/// In-memory transport store. RunLockedAsync mirrors the real contract: on a failed result the
/// changes made inside are undone (assignment statuses restored, added rows removed).
/// </summary>
internal sealed class FakeTransportationRepository : ITransportationRepository
{
    public List<Driver> Drivers { get; } = new();
    public List<Route> Routes { get; } = new();
    public List<Bus> Buses { get; } = new();
    public List<Student> Students { get; } = new();
    public List<StudentBusAssignment> Assignments { get; } = new();
    public List<TransportAuditEntry> Audit { get; } = new();
    public List<(int[] Students, int[] Buses)> Locks { get; } = new();
    private int _id = 100;

    public Student AddStudent(string name = "Kid")
    {
        var s = new Student { StudentId = ++_id, FullName = name, DateOfBirth = new DateTime(2016, 1, 1) };
        Students.Add(s);
        return s;
    }

    public Bus AddBus(string number, int capacity, int? routeId = null, BusStatus status = BusStatus.Active, int? driverId = null)
    {
        var b = new Bus { BusId = ++_id, BusNumber = number, Capacity = capacity, RouteId = routeId, DriverId = driverId, Status = status };
        Buses.Add(b);
        return b;
    }

    public Route AddRoute(string name, RecordStatus status = RecordStatus.Active)
    {
        var r = new Route { RouteId = ++_id, RouteName = name, Status = status };
        Routes.Add(r);
        return r;
    }

    public Driver AddDriver(string name, RecordStatus status = RecordStatus.Active)
    {
        var d = new Driver { DriverId = ++_id, FullName = name, Status = status };
        Drivers.Add(d);
        return d;
    }

    public StudentBusAssignment Seat(int studentId, int busId, int? routeId = null)
    {
        var a = new StudentBusAssignment { Id = ++_id, StudentId = studentId, BusId = busId, RouteId = routeId, Status = RecordStatus.Active };
        Assignments.Add(a);
        return a;
    }

    public Task<List<Driver>> GetDriversAsync(CancellationToken ct = default) => Task.FromResult(Drivers.ToList());
    public Task AddDriverAsync(Driver driver, CancellationToken ct = default) { driver.DriverId = ++_id; Drivers.Add(driver); return Task.CompletedTask; }
    public Task<List<Route>> GetRoutesAsync(CancellationToken ct = default) => Task.FromResult(Routes.ToList());
    public Task AddRouteAsync(Route route, CancellationToken ct = default) { route.RouteId = ++_id; Routes.Add(route); return Task.CompletedTask; }
    public Task<List<Bus>> GetBusesAsync(CancellationToken ct = default) => Task.FromResult(Buses.ToList());
    public Task<Bus?> GetBusByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Buses.FirstOrDefault(b => b.BusId == id));
    public Task AddBusAsync(Bus bus, CancellationToken ct = default) { bus.BusId = ++_id; Buses.Add(bus); return Task.CompletedTask; }

    public Task<List<StudentBusAssignment>> GetAssignmentsAsync(CancellationToken ct = default)
    {
        foreach (var a in Assignments)
        {
            a.Student = Students.First(s => s.StudentId == a.StudentId);
            a.Bus = Buses.First(b => b.BusId == a.BusId);
            a.Route = Routes.FirstOrDefault(r => r.RouteId == a.RouteId);
        }
        return Task.FromResult(Assignments.ToList());
    }

    public Task<Student?> GetStudentAsync(int studentId, CancellationToken ct = default) => Task.FromResult(Students.FirstOrDefault(s => s.StudentId == studentId));
    public Task AddAssignmentAsync(StudentBusAssignment assignment, CancellationToken ct = default) { assignment.Id = ++_id; Assignments.Add(assignment); return Task.CompletedTask; }
    public Task<Driver?> GetDriverByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Drivers.FirstOrDefault(d => d.DriverId == id));
    public Task<Route?> GetRouteByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Routes.FirstOrDefault(r => r.RouteId == id));
    public Task<StudentBusAssignment?> GetAssignmentByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(Assignments.FirstOrDefault(a => a.Id == id));
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Mimics the database's unique key on active assignments.</summary>
    public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) =>
        Task.FromResult(Assignments.Where(a => a.Status == RecordStatus.Active).GroupBy(a => a.StudentId).All(g => g.Count() == 1));

    public async Task<Result> RunLockedAsync(IReadOnlyCollection<int> studentIds, IReadOnlyCollection<int> busIds,
        Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        Locks.Add((studentIds.ToArray(), busIds.ToArray()));
        var snapshot = Assignments.Select(a => (a, a.Status, a.EndedAt)).ToList();
        var count = Assignments.Count;
        var result = await work(ct);
        if (!result.Succeeded)
        {
            Assignments.RemoveRange(count, Assignments.Count - count);
            foreach (var (a, status, ended) in snapshot) { a.Status = status; a.EndedAt = ended; }
        }
        return result;
    }

    public Task<int> CountActiveAssignmentsAsync(int busId, CancellationToken ct = default) =>
        Task.FromResult(Assignments.Count(a => a.BusId == busId && a.Status == RecordStatus.Active));

    public Task<Dictionary<int, int>> GetActiveAssignmentCountsAsync(CancellationToken ct = default) =>
        Task.FromResult(Assignments.Where(a => a.Status == RecordStatus.Active).GroupBy(a => a.BusId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<StudentBusAssignment?> GetActiveAssignmentForStudentAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(Assignments.FirstOrDefault(a => a.StudentId == studentId && a.Status == RecordStatus.Active));

    public async Task<List<StudentBusAssignment>> GetActiveRosterAsync(IReadOnlyCollection<int> busIds, CancellationToken ct = default) =>
        (await GetAssignmentsAsync(ct)).Where(a => busIds.Contains(a.BusId) && a.Status == RecordStatus.Active).ToList();
    public Task<bool> EmployeeCodeTakenAsync(string employeeCode, int exceptDriverId, CancellationToken ct = default) =>
        Task.FromResult(Drivers.Any(d => d.DriverId != exceptDriverId && string.Equals(d.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase)));
    public Task<bool> RegistrationTakenAsync(string registrationNumber, int exceptBusId, CancellationToken ct = default) =>
        Task.FromResult(Buses.Any(b => b.BusId != exceptBusId && string.Equals(b.RegistrationNumber, registrationNumber, StringComparison.OrdinalIgnoreCase)));
    public Task<(int Assignments, int JourneyEvents)> CountBusHistoryAsync(int busId, CancellationToken ct = default) =>
        Task.FromResult((Assignments.Count(a => a.BusId == busId), 0));
    public List<RouteStop> Stops { get; } = new();
    public Task<List<RouteStop>> GetStopsAsync(int routeId, CancellationToken ct = default) =>
        Task.FromResult(Stops.Where(s => s.RouteId == routeId).OrderBy(s => s.Sequence).ToList());
    public Task<RouteStop?> GetStopAsync(int stopId, CancellationToken ct = default) => Task.FromResult(Stops.FirstOrDefault(s => s.RouteStopId == stopId));
    public Task AddStopAsync(RouteStop stop, CancellationToken ct = default) { stop.RouteStopId = ++_id; Stops.Add(stop); return Task.CompletedTask; }
    public void RemoveStop(RouteStop stop) => Stops.Remove(stop);
    public Task<int> CountStopUsageAsync(int stopId, CancellationToken ct = default) =>
        Task.FromResult(Assignments.Count(a => a.PickupStopId == stopId || a.DropStopId == stopId));
    public Task<Dictionary<int, int>> CountActiveStopUsageAsync(int routeId, CancellationToken ct = default) => Task.FromResult(new Dictionary<int, int>());
    public Task<(int Buses, int Assignments, int JourneyEvents)> CountRouteHistoryAsync(int routeId, CancellationToken ct = default) =>
        Task.FromResult((Buses.Count(b => b.RouteId == routeId), Assignments.Count(a => a.RouteId == routeId), 0));
    public void RemoveRoute(Route route) => Routes.Remove(route);
    public async Task<List<StudentBusAssignment>> GetActiveRosterByRouteAsync(int routeId, CancellationToken ct = default) =>
        (await GetAssignmentsAsync(ct)).Where(a => a.RouteId == routeId && a.Status == RecordStatus.Active).ToList();
    public void RemoveDriver(Driver driver) => Drivers.Remove(driver);
    public void RemoveBus(Bus bus) => Buses.Remove(bus);

    public Task AddAuditAsync(TransportAuditEntry entry, CancellationToken ct = default) { Audit.Add(entry); return Task.CompletedTask; }

    public Task<List<(TransportAuditEntry Entry, string? AdminName)>> GetAuditAsync(int take, CancellationToken ct = default) =>
        Task.FromResult(Audit.AsEnumerable().Reverse().Take(take).Select(e => (e, (string?)null)).ToList());
}
