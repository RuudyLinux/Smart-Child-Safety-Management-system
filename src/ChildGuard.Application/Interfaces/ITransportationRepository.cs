using ChildGuard.Application.Common;
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

    /// <summary>
    /// Saves pending changes. Returns false, with nothing saved and the pending changes
    /// discarded, when a unique index refused them (e.g. a second active assignment).
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction while holding row locks on the given
    /// students and buses (students first, then buses, each in id order, so two callers can
    /// never deadlock). Two assignments to the last seat of a bus, or two changes for the same
    /// student, are applied one after the other instead of both passing their checks.
    /// The transaction commits only when <paramref name="work"/> succeeds; a failed result rolls
    /// back everything it saved, so a move can never end the old assignment without the new one.
    /// </summary>
    Task<Result> RunLockedAsync(IReadOnlyCollection<int> studentIds, IReadOnlyCollection<int> busIds,
        Func<CancellationToken, Task<Result>> work, CancellationToken ct = default);

    Task<int> CountActiveAssignmentsAsync(int busId, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetActiveAssignmentCountsAsync(CancellationToken ct = default);
    Task<StudentBusAssignment?> GetActiveAssignmentForStudentAsync(int studentId, CancellationToken ct = default);

    /// <summary>Active assignments on these buses with student, bus and route loaded (the live roster).</summary>
    Task<List<StudentBusAssignment>> GetActiveRosterAsync(IReadOnlyCollection<int> busIds, CancellationToken ct = default);
    Task<bool> EmployeeCodeTakenAsync(string employeeCode, int exceptDriverId, CancellationToken ct = default);
    Task<bool> RegistrationTakenAsync(string registrationNumber, int exceptBusId, CancellationToken ct = default);
    /// <summary>Assignments ever made to the bus (active or ended) and journey events recorded on it.</summary>
    Task<(int Assignments, int JourneyEvents)> CountBusHistoryAsync(int busId, CancellationToken ct = default);
    /// <summary>All stops of the route (any status), in travel order.</summary>
    Task<List<RouteStop>> GetStopsAsync(int routeId, CancellationToken ct = default);
    Task<RouteStop?> GetStopAsync(int stopId, CancellationToken ct = default);
    Task AddStopAsync(RouteStop stop, CancellationToken ct = default);
    void RemoveStop(RouteStop stop);
    /// <summary>Assignments (current or past) that board or leave at the stop.</summary>
    Task<int> CountStopUsageAsync(int stopId, CancellationToken ct = default);
    /// <summary>Per stop of the route: active assignments boarding or leaving there.</summary>
    Task<Dictionary<int, int>> CountActiveStopUsageAsync(int routeId, CancellationToken ct = default);
    Task<(int Buses, int Assignments, int JourneyEvents)> CountRouteHistoryAsync(int routeId, CancellationToken ct = default);
    void RemoveRoute(Route route);
    /// <summary>Active assignments travelling on the route (the route's live roster).</summary>
    Task<List<StudentBusAssignment>> GetActiveRosterByRouteAsync(int routeId, CancellationToken ct = default);
    void RemoveDriver(Driver driver);
    void RemoveBus(Bus bus);
    Task AddAuditAsync(TransportAuditEntry entry, CancellationToken ct = default);
    Task<List<(TransportAuditEntry Entry, string? AdminName)>> GetAuditAsync(int take, CancellationToken ct = default);
}
