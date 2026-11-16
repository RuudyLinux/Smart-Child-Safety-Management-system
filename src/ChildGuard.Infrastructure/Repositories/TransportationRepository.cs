using System.Data;
using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
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

    public async Task<bool> TrySaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<Result> RunLockedAsync(IReadOnlyCollection<int> studentIds, IReadOnlyCollection<int> busIds,
        Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        // Inside a caller's transaction (enrollment): take the same locks there and leave commit or
        // rollback to the caller, so a failed assignment undoes everything the caller wrote too.
        if (_db.Database.CurrentTransaction is not null)
        {
            foreach (var id in studentIds.Distinct().OrderBy(i => i)) await LockStudentAsync(id, ct);
            foreach (var id in busIds.Distinct().OrderBy(i => i)) await LockBusAsync(id, ct);
            return await work(ct);
        }

        // READ COMMITTED so counts read after the lock see whatever the previous holder committed.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // Fixed order (students, then buses, ascending ids) on every path, so two callers never deadlock.
        foreach (var id in studentIds.Distinct().OrderBy(i => i)) await LockStudentAsync(id, ct);
        foreach (var id in busIds.Distinct().OrderBy(i => i)) await LockBusAsync(id, ct);

        var result = await work(ct);
        if (result.Succeeded) await tx.CommitAsync(ct);
        else
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
        }
        return result;
    }

    // Row locks held until commit (InnoDB / PostgreSQL), written like JourneyRepository's student lock:
    // the SQL text is fixed and the id is sent as a parameter.
    private Task LockStudentAsync(int id, CancellationToken ct) =>
        _db.Database.IsNpgsql()
            ? _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"StudentId\" FROM \"Students\" WHERE \"StudentId\" = {id} FOR UPDATE", ct)
            : _db.Database.ExecuteSqlInterpolatedAsync($"SELECT StudentId FROM Students WHERE StudentId = {id} FOR UPDATE", ct);

    private Task LockBusAsync(int id, CancellationToken ct) =>
        _db.Database.IsNpgsql()
            ? _db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"BusId\" FROM \"Buses\" WHERE \"BusId\" = {id} FOR UPDATE", ct)
            : _db.Database.ExecuteSqlInterpolatedAsync($"SELECT BusId FROM Buses WHERE BusId = {id} FOR UPDATE", ct);

    public Task<int> CountActiveAssignmentsAsync(int busId, CancellationToken ct = default) =>
        _db.StudentBusAssignments.CountAsync(a => a.BusId == busId && a.Status == RecordStatus.Active, ct);

    public Task<Dictionary<int, int>> GetActiveAssignmentCountsAsync(CancellationToken ct = default) =>
        _db.StudentBusAssignments.AsNoTracking()
            .Where(a => a.Status == RecordStatus.Active)
            .GroupBy(a => a.BusId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public Task<StudentBusAssignment?> GetActiveAssignmentForStudentAsync(int studentId, CancellationToken ct = default) =>
        _db.StudentBusAssignments.FirstOrDefaultAsync(a => a.StudentId == studentId && a.Status == RecordStatus.Active, ct);

    public async Task AddAuditAsync(TransportAuditEntry entry, CancellationToken ct = default) =>
        await _db.TransportAuditEntries.AddAsync(entry, ct);

    public async Task<List<(TransportAuditEntry Entry, string? AdminName)>> GetAuditAsync(int take, CancellationToken ct = default)
    {
        var rows = await (from e in _db.TransportAuditEntries.AsNoTracking()
                          join a in _db.Admins.AsNoTracking() on e.AdminId equals a.AdminId into admins
                          from a in admins.DefaultIfEmpty()
                          orderby e.OccurredAt descending, e.Id descending
                          select new { Entry = e, AdminName = a == null ? null : a.FullName })
            .Take(take).ToListAsync(ct);
        return rows.Select(r => (r.Entry, (string?)r.AdminName)).ToList();
    }
}
